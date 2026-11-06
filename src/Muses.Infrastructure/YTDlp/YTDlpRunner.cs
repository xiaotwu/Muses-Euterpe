using System.Diagnostics;
using System.Text;

namespace Muses.Infrastructure.YTDlp;

public sealed class YTDlpRunner
{
    private readonly SemaphoreSlim _slots;

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
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteAsync(executablePath, args, timeout, ct, publicAudio).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
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
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
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
