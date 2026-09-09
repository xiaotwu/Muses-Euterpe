using System.Diagnostics;
using System.Text.Json;

namespace Muses.Infrastructure.Playback;

/// <summary>
/// Production mpv launcher: resolves the binary, starts a no-video process with
/// <c>--input-ipc-server</c>, and exposes pause/seek/volume/EOF over JSON IPC.
/// </summary>
public sealed class MpvPlayerFactory : IMpvPlayerFactory
{
    private readonly Func<string?>? _binaryResolver;

    public MpvPlayerFactory(Func<string?>? binaryResolver = null)
    {
        _binaryResolver = binaryResolver;
    }

    public string? FindBinary() => _binaryResolver?.Invoke() ?? LocateMpvBinary();

    public IMpvPlayerSession Start(Uri url, int initialVolume0to100)
    {
        var binary = FindBinary()
            ?? throw new FileNotFoundException("mpv binary was not found on PATH or under resources/.");

        var volume = Math.Clamp(initialVolume0to100, 0, 100);
        var windows = OperatingSystem.IsWindows();
        var endpoint = windows
            ? $"muses-mpv-{Guid.NewGuid():N}"
            : Path.Combine(Path.GetTempPath(), $"muses-mpv-{Guid.NewGuid():N}.sock");

        if (!windows && File.Exists(endpoint))
        {
            try { File.Delete(endpoint); } catch { /* ignore */ }
        }

        var ipcArg = windows
            ? $"--input-ipc-server=\\\\.\\pipe\\{endpoint}"
            : $"--input-ipc-server={endpoint}";

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in new[]
                 {
                     "--no-video",
                     "--no-terminal",
                     "--really-quiet",
                     "--idle=no",
                     "--keep-open=no",
                     "--gapless-audio=weak",
                     ipcArg,
                     $"--volume={volume}",
                     "--",
                     url.ToString()
                 })
            psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("Failed to start mpv process.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to start mpv: {ex.Message}", ex);
        }

        var ipc = new MpvIpcClient();
        try
        {
            // Connect synchronously with a short timeout so Start() fails fast.
            ipc.ConnectAsync(endpoint, windows, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
            process.Dispose();
            ipc.Dispose();
            if (!windows)
            {
                try { if (File.Exists(endpoint)) File.Delete(endpoint); } catch { /* ignore */ }
            }
            throw;
        }

        return new MpvPlayerSession(process, ipc, endpoint, windows);
    }

    public static string? LocateMpvBinary()
    {
        var exe = OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        if (FindOnPath(exe) is { } fromPath) return fromPath;

        foreach (var root in CandidateResourceRoots())
        {
            foreach (var rel in new[]
                     {
                         exe,
                         Path.Combine("mpv", exe),
                         Path.Combine("bin", exe),
                         Path.Combine("resources", exe),
                         Path.Combine("resources", "mpv", exe),
                         Path.Combine("resources", "bin", exe)
                     })
            {
                var candidate = Path.Combine(root, rel);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateResourceRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var full = Path.GetFullPath(p);
                if (seen.Add(full)) { /* keep */ }
            }
            catch { /* ignore bad paths */ }
        }

        Add(AppContext.BaseDirectory);
        Add(Path.GetDirectoryName(Environment.ProcessPath));
        Add(Directory.GetCurrentDirectory());

        // Walk up a few levels from BaseDirectory to catch repo `resources/` while developing.
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
                Add(dir.FullName);
        }
        catch { /* ignore */ }

        return seen;
    }

    private static string? FindOnPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var candidate = Path.Combine(p.Trim(), name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private sealed class MpvPlayerSession : IMpvPlayerSession
    {
        private readonly Process _process;
        private readonly MpvIpcClient _ipc;
        private readonly string _endpoint;
        private readonly bool _windows;
        private int _disposed;
        private int _eofRaised;
        private double _timePos;

        public MpvPlayerSession(Process process, MpvIpcClient ipc, string endpoint, bool windows)
        {
            _process = process;
            _ipc = ipc;
            _endpoint = endpoint;
            _windows = windows;
            _ipc.EventReceived += OnEvent;
            _ipc.Disconnected += OnDisconnected;
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                try { Exited?.Invoke(); } catch { /* ignore */ }
            };
        }

        public bool IsAlive => _disposed == 0 && !_process.HasExited;
        public double TimePos => _timePos;

        public event Action? EndOfFile;
        public event Action? Exited;

        public void SetPause(bool paused) =>
            _ = FireAndForget(["set_property", "pause", paused]);

        public void SeekAbsolute(double seconds)
        {
            _timePos = Math.Max(0, seconds);
            _ = FireAndForget(["seek", seconds, "absolute"]);
        }

        public void SetVolume(int volume0to100) =>
            _ = FireAndForget(["set_property", "volume", Math.Clamp(volume0to100, 0, 100)]);

        public void SetAudioFilters(string? afChain) =>
            _ = FireAndForget(["set_property", "af", afChain ?? ""]);

        public async Task<double?> GetTimePosAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAlive) return null;
            try
            {
                var data = await _ipc.RequestAsync(["get_property", "time-pos"], cancellationToken)
                    .ConfigureAwait(false);
                if (data is { ValueKind: JsonValueKind.Number } el)
                {
                    _timePos = el.GetDouble();
                    return _timePos;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private void OnEvent(string name, JsonElement root)
        {
            if (!string.Equals(name, "end-file", StringComparison.Ordinal)) return;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            if (!string.Equals(reason, "eof", StringComparison.Ordinal)) return;
            if (Interlocked.Exchange(ref _eofRaised, 1) != 0) return;
            try { EndOfFile?.Invoke(); } catch { /* ignore */ }
        }

        private void OnDisconnected()
        {
            // Process exit / socket close is surfaced via Exited; nothing else to do.
        }

        private async Task FireAndForget(object[] command)
        {
            if (!IsAlive) return;
            try { await _ipc.SendAsync(command).ConfigureAwait(false); }
            catch { /* ignore command failures on a dying session */ }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _ipc.EventReceived -= OnEvent;
            _ipc.Disconnected -= OnDisconnected;
            try { _ipc.Dispose(); } catch { /* ignore */ }
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
            try { _process.Dispose(); } catch { /* ignore */ }

            if (!_windows)
            {
                try { if (File.Exists(_endpoint)) File.Delete(_endpoint); } catch { /* ignore */ }
            }
        }
    }
}
