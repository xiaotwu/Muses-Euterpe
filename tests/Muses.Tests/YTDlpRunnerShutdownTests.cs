using Muses.Infrastructure.YTDlp;

namespace Muses.Tests;

public sealed class YTDlpRunnerShutdownTests
{
    [LinuxFact]
    public async Task ShutdownCancelsRunningTreeAndWorkWaitingForSlot()
    {
        var workspace = Directory.CreateTempSubdirectory("muses-ytdlp-shutdown-");
        using var runner = new YTDlpRunner(maxConcurrent: 1);
        try
        {
            var pidFile = Path.Combine(workspace.FullName, "pids");
            var active = runner.RunAsync("/bin/sh",
                ["-c", """sleep 60 & printf '%s %s\n' "$$" "$!" > "$1"; wait""", "test-resolver", pidFile],
                TimeSpan.FromSeconds(30));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string[] pids;
            do
            {
                await Task.Delay(20, deadline.Token);
                var contents = File.Exists(pidFile) ? await File.ReadAllTextAsync(pidFile, deadline.Token) : "";
                pids = contents.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            } while (pids.Length != 2 || pids.Any(pid => !IsRunning(pid)));

            var marker = Path.Combine(workspace.FullName, "queued-started");
            var queued = runner.RunAsync("/bin/sh", ["-c", """touch "$1" """, "queued-resolver", marker], TimeSpan.FromSeconds(30));

            runner.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.RunAsync("/bin/sh", [], TimeSpan.FromSeconds(1)));
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

    private static bool IsRunning(string pid)
    {
        try
        {
            // A killed child may briefly remain as a zombie until its new parent reaps it.
            var state = File.ReadLines($"/proc/{pid}/status").FirstOrDefault(line => line.StartsWith("State:", StringComparison.Ordinal));
            return state is not null && !state.StartsWith("State:\tZ", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
                Skip = "Requires Linux process status and the system shell.";
        }
    }
}
