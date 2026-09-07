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

    public async Task<(string Stdout, string Stderr)> RunAsync(
        string executablePath, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
    {
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteAsync(executablePath, args, timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    private static async Task<(string Stdout, string Stderr)> ExecuteAsync(
        string executablePath, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(executablePath))
            throw new YTDlpException("yt-dlp binary not found");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new YTDlpException("yt-dlp binary not found");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new YTDlpException("yt-dlp timed out");
        }

        if (process.ExitCode != 0)
            throw new YTDlpException($"yt-dlp exit code {process.ExitCode}:{stderr}");

        return (stdout.ToString(), stderr.ToString());
    }
}
