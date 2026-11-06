using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Muses.Core.Advanced;

namespace Muses.Infrastructure.Advanced;

/// <summary>
/// Launches the isolated <c>MusesWebHomeHelper</c> process over stdin/stdout JSON.
/// Cookie materialization happens only inside the helper; this process never scrapes.
/// </summary>
public sealed class ProcessWebHomeHelperClient : IWebHomeHelperClient, IDisposable
{
    private const int MaximumRequestBytes = 64 * 1024;
    private static readonly TimeSpan CleanupGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _helperPath;
    private readonly int _maximumOutputBytes;
    private readonly object _gate = new();
    private ActiveRequest? _active;
    private bool _disposed;

    public ProcessWebHomeHelperClient(string? helperPath = null, int maximumOutputBytes = 10 * 1024 * 1024)
    {
        _helperPath = helperPath ?? LocateHelper() ?? "";
        _maximumOutputBytes = maximumOutputBytes;
    }

    public async Task<WebHomeResponse> ExecuteAsync(WebHomeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_helperPath) || !File.Exists(_helperPath))
            return WebHomeResponse.Unavailable("helperMissing", "Web Home helper binary was not found.");

        if (request.ProtocolVersion != WebHomeProtocolVersion.Current)
            return WebHomeResponse.Unavailable("protocolMismatch");

        var workspaceId = Guid.NewGuid();
        byte[] requestBytes;
        try
        {
            requestBytes = JsonSerializer.SerializeToUtf8Bytes(request with { WorkspaceId = workspaceId }, JsonOptions);
        }
        catch
        {
            return WebHomeResponse.Unavailable("malformedResponse");
        }
        if (requestBytes.Length > MaximumRequestBytes)
            return WebHomeResponse.Unavailable("malformedResponse");
        Array.Resize(ref requestBytes, requestBytes.Length + 1);
        requestBytes[^1] = (byte)'\n';

        var psi = new ProcessStartInfo
        {
            FileName = _helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process process;
        ActiveRequest active;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_active is not null)
                return WebHomeResponse.Unavailable("helperBusy", "A Web Home helper request is already in flight.");
            try
            {
                process = Process.Start(psi) ?? throw new InvalidOperationException("start failed");
            }
            catch (Exception ex)
            {
                return WebHomeResponse.Unavailable("helperCrashed", ex.GetType().Name);
            }
            _active = active = new ActiveRequest();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Clamp(request.TimeoutMs, 1000, 12000)));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, active.Stop.Token, timeout.Token);
        var ct = lifetime.Token;
        try
        {
            var stdoutTask = ReadLimitedAsync(process.StandardOutput.BaseStream, _maximumOutputBytes, ct);
            var stderrTask = ReadLimitedAsync(process.StandardError.BaseStream, 0, ct);
            await process.StandardInput.BaseStream.WriteAsync(requestBytes, ct).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var (data, exceeded) = await stdoutTask.ConfigureAwait(false);
            if (exceeded)
                return WebHomeResponse.Unavailable("responseTooLarge");
            if (process.ExitCode != 0)
                return WebHomeResponse.Unavailable("helperCrashed", $"exit {process.ExitCode}");

            try
            {
                var response = JsonSerializer.Deserialize<WebHomeResponse>(data, JsonOptions);
                if (response is null)
                    return WebHomeResponse.Unavailable("malformedResponse");
                if (response.ProtocolVersion != WebHomeProtocolVersion.Current)
                    return WebHomeResponse.Unavailable("protocolMismatch");
                return response;
            }
            catch (JsonException)
            {
                return WebHomeResponse.Unavailable("malformedResponse");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WebHomeResponse.Unavailable(active.Stop.IsCancellationRequested ? "cancelled" : "timedOut");
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WebHomeResponse.Unavailable("helperCrashed");
        }
        finally
        {
            try
            {
                if (await StopProcessAsync(process).ConfigureAwait(false))
                    DeleteWorkspace(workspaceId);
            }
            finally
            {
                process.Dispose();
                lock (_gate)
                {
                    _active = null;
                    active.Stop.Dispose();
                }
                active.Completed.TrySetResult();
            }
        }
    }

    public Task CancelAsync()
    {
        lock (_gate)
        {
            if (_active is not { } active) return Task.CompletedTask;
            active.Stop.Cancel();
            return active.Completed.Task;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        CancelAsync().GetAwaiter().GetResult();
    }

    private static async Task<bool> StopProcessAsync(Process process)
    {
        // Closing stdin is the helper's cancellation signal. Give it time to dispose the jar.
        try { process.StandardInput.Close(); }
        catch (IOException) { }
        if (process.HasExited) return true;

        using var grace = new CancellationTokenSource(CleanupGracePeriod);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        using var killWait = new CancellationTokenSource(CleanupGracePeriod);
        try
        {
            await process.WaitForExitAsync(killWait.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (killWait.IsCancellationRequested)
        {
            return process.HasExited;
        }
    }

    private static void DeleteWorkspace(Guid id)
    {
        try
        {
            var workspace = new DirectoryInfo(WebHomeWorkspace.GetPath(id));
            if (workspace.Exists && !workspace.Attributes.HasFlag(FileAttributes.ReparsePoint)
                && !workspace.Parent!.Attributes.HasFlag(FileAttributes.ReparsePoint))
                workspace.Delete(recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<(byte[] Data, bool Exceeded)> ReadLimitedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        var exceeded = false;
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            if (read <= 0) break;
            if (ms.Length + read <= limit)
                ms.Write(buffer, 0, read);
            else
                exceeded = true;
        }
        return (ms.ToArray(), exceeded);
    }

    private sealed class ActiveRequest
    {
        public CancellationTokenSource Stop { get; } = new();
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static string? LocateHelper()
    {
        const string name = "MusesWebHomeHelper.exe";
        var candidates = new List<string>();
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, name));
        candidates.Add(Path.Combine(baseDir, "Helpers", name));
        // Dev: walk up to repo artifacts / sibling project output
        try
        {
            var dir = new DirectoryInfo(baseDir);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "src", "Muses.WebHome.Helper", "bin", "Debug", "net10.0", name));
                candidates.Add(Path.Combine(dir.FullName, "src", "Muses.WebHome.Helper", "bin", "Release", "net10.0", name));
            }
        }
        catch { /* ignore */ }

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }
}
