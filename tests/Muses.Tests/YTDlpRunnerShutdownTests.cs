using System.Diagnostics;
using Muses.Infrastructure.YTDlp;

namespace Muses.Tests;

public sealed class YTDlpRunnerShutdownTests
{
    [Fact]
    public async Task ShutdownCancelsRunningTreeAndWorkWaitingForSlot()
    {
        var workspace = Directory.CreateTempSubdirectory("muses-ytdlp-shutdown-");
        using var runner = new YTDlpRunner(maxConcurrent: 1);
        try
        {
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var fixture = Path.Combine(workspace.FullName, "resolver.ps1");
            await File.WriteAllTextAsync(fixture, """
                param([string]$PidFile)
                $ErrorActionPreference = 'Stop'
                $start = @{
                    FilePath = Join-Path $PSHOME 'powershell.exe'
                    WindowStyle = 'Hidden'
                    PassThru = $true
                    ArgumentList = '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 60'
                }
                $child = Start-Process @start
                [IO.File]::WriteAllText($PidFile, "$PID $($child.Id)")
                $child.WaitForExit()
                """);
            var pidFile = Path.Combine(workspace.FullName, "pids");
            var active = runner.RunAsync(powershell,
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture, pidFile],
                TimeSpan.FromSeconds(30));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string[] pids;
            do
            {
                await Task.Delay(20, deadline.Token);
                var contents = File.Exists(pidFile) ? await File.ReadAllTextAsync(pidFile, deadline.Token) : "";
                pids = contents.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            } while (pids.Length != 2 || pids.Any(pid => !IsRunning(pid)));

            var marker = Path.Combine(workspace.FullName, "queued-started");
            var queued = runner.RunAsync(powershell,
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture, marker],
                TimeSpan.FromSeconds(30));

            runner.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.RunAsync(powershell, [], TimeSpan.FromSeconds(1)));
            Assert.False(File.Exists(marker));
            Assert.True(SpinWait.SpinUntil(() => !pids.Any(IsRunning), TimeSpan.FromSeconds(1)),
                "A resolver process survived shutdown.");
        }
        finally
        {
            runner.Dispose();
            workspace.Delete(recursive: true);
        }
    }

    private static bool IsRunning(string value)
    {
        if (!int.TryParse(value, out var pid))
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
