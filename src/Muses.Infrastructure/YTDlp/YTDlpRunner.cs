using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Muses.Infrastructure.YTDlp;

public sealed class YTDlpRunner : IDisposable
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _slots;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task> _pending = [];
    private bool _disposed;

    public YTDlpRunner(int maxConcurrent = 2)
    {
        _slots = new SemaphoreSlim(Math.Max(1, maxConcurrent), Math.Max(1, maxConcurrent));
    }

    public Task<(string Stdout, string Stderr)> RunAsync(
        string executablePath, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        => RunLimitedAsync(executablePath, args, timeout, ct, publicAudio: false);

    internal async Task<string> RunAudioAsync(
        string executablePath, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var (stdout, _) = await RunLimitedAsync(executablePath, args, timeout, ct, publicAudio: true).ConfigureAwait(false);
        return stdout;
    }

    private async Task<(string Stdout, string Stderr)> RunLimitedAsync(
        string executablePath, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct, bool publicAudio)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            _pending.Add(completed.Task);
        }

        try
        {
            await _slots.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                return await ExecuteAsync(executablePath, args, timeout, cancellation.Token, publicAudio)
                    .ConfigureAwait(false);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        finally
        {
            cancellation.Dispose();
            lock (_gate)
            {
                completed.TrySetResult();
                _pending.Remove(completed.Task);
            }
        }
    }

    public void Dispose()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = [.. _pending];
        }

        // Process-tree termination can still be running after cancellation is signaled.
        _shutdown.Cancel();
        if (!Task.WaitAll(pending, ShutdownTimeout))
            Trace.TraceWarning("Timed out waiting for yt-dlp shutdown.");
        _shutdown.Dispose();
    }

    private static async Task<(string Stdout, string Stderr)> ExecuteAsync(
        string executablePath, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct, bool publicAudio)
    {
        if (!File.Exists(executablePath))
            throw new YTDlpException("yt-dlp binary not found");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["DENO_DIR"] = Path.Combine(LocalDataMaintenance.LocalCacheRoot(), "deno");
        process.StartInfo.Environment["DENO_NO_UPDATE_CHECK"] = "1";

        ct.ThrowIfCancellationRequested();
        if (!process.Start())
            throw new YTDlpException("yt-dlp binary not found");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var stdout = ReadBoundedAsync(process.StandardOutput,
            publicAudio ? AudioStreamJsonParser.MaximumBytes : 64 * 1024 * 1024, timeoutCts.Token);
        var stderr = ReadBoundedAsync(process.StandardError, 1024 * 1024, timeoutCts.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeoutCts.Token), stdout, stderr)
                .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            throw new YTDlpException("yt-dlp timed out");
        }

        var (output, outputTruncated) = await stdout.ConfigureAwait(false);
        var (error, _) = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new YTDlpException($"yt-dlp exit code {process.ExitCode}: {(publicAudio ? ErrorCategory(error) : error)}");
        if (outputTruncated)
            throw new YTDlpException("yt-dlp output exceeded limit");

        return (output, error);
    }

    private static async Task StopProcessAsync(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or AggregateException)
        {
            if (!process.HasExited)
                Trace.TraceWarning("Could not terminate yt-dlp: {0}", error.GetType().Name);
        }

        using var deadline = new CancellationTokenSource(ProcessExitTimeout);
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Trace.TraceWarning("Timed out waiting for yt-dlp to exit.");
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            var remaining = limit - text.Length;
            truncated |= count > remaining;
            text.Append(buffer, 0, Math.Min(remaining, count));
        }
        return (text.ToString(), truncated);
    }

    internal static string ErrorCategory(string stderr)
    {
        string[] categories = ["http error 429", "http error 403", "sign in", "private video", "video unavailable",
            "age-restricted", "not available in your country", "javascript runtime", "challenge",
            "requested format is not available", "timed out", "connection"];
        return categories.FirstOrDefault(category => stderr.Contains(category, StringComparison.OrdinalIgnoreCase))
            ?? "extraction failed";
    }
}
