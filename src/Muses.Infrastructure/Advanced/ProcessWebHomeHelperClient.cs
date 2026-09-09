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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _helperPath;
    private readonly int _maximumOutputBytes;
    private readonly object _gate = new();
    private Process? _active;
    private int _disposed;

    public ProcessWebHomeHelperClient(string? helperPath = null, int maximumOutputBytes = 10 * 1024 * 1024)
    {
        _helperPath = helperPath ?? LocateHelper() ?? "";
        _maximumOutputBytes = maximumOutputBytes;
    }

    public async Task<WebHomeResponse> ExecuteAsync(WebHomeRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (string.IsNullOrWhiteSpace(_helperPath) || !File.Exists(_helperPath))
            return WebHomeResponse.Unavailable("helperMissing", "Web Home helper binary was not found.");

        if (request.ProtocolVersion != WebHomeProtocolVersion.Current)
            return WebHomeResponse.Unavailable("protocolMismatch");

        byte[] requestBytes;
        try
        {
            requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        }
        catch
        {
            return WebHomeResponse.Unavailable("malformedResponse");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process process;
        lock (_gate)
        {
            if (_active is { HasExited: false })
                return WebHomeResponse.Unavailable("helperBusy", "A Web Home helper request is already in flight.");
            try
            {
                process = Process.Start(psi) ?? throw new InvalidOperationException("start failed");
            }
            catch (Exception ex)
            {
                return WebHomeResponse.Unavailable("helperCrashed", ex.Message);
            }
            _active = process;
        }

        try
        {
            await process.StandardInput.BaseStream.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1000, request.TimeoutMs)));

            var stdoutTask = ReadLimitedAsync(process.StandardOutput.BaseStream, _maximumOutputBytes, cts.Token);
            _ = ReadLimitedAsync(process.StandardError.BaseStream, 64 * 1024, CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return WebHomeResponse.Unavailable("timedOut", "Web Home helper timed out.");
            }

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
            catch
            {
                return WebHomeResponse.Unavailable("malformedResponse");
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, process))
                    _active = null;
            }
            try { process.Dispose(); } catch { /* ignore */ }
        }
    }

    public Task CancelAsync()
    {
        Process? proc;
        lock (_gate) { proc = _active; }
        if (proc is not null) TryKill(proc);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ = CancelAsync();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }
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
            catch (OperationCanceledException)
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

    internal static string? LocateHelper()
    {
        var name = OperatingSystem.IsWindows() ? "MusesWebHomeHelper.exe" : "MusesWebHomeHelper";
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
