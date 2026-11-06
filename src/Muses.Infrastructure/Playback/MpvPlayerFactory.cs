using System.Diagnostics;
using System.Text.Json;
using Muses.Core.Platform;
using Muses.Infrastructure.YTDlp;

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

    public async Task<IMpvPlayerSession> StartAsync(
        ResolvedAudioStream stream, int initialVolume0to100, CancellationToken cancellationToken)
    {
        var binary = FindBinary()
            ?? throw new FileNotFoundException("mpv could not be found.");

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
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var a in new[]
                 {
                     "--no-config",
                     "--no-ytdl",
                     "--no-video",
                     "--force-window=no",
                     "--no-terminal",
                     "--really-quiet",
                     "--idle=yes",
                     "--pause",
                     "--keep-open=no",
                     "--gapless-audio=weak",
                     ipcArg,
                     $"--volume={volume}"
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
        var session = new MpvPlayerSession(process, ipc, endpoint, windows);
        try
        {
            await ipc.ConnectAsync(endpoint, windows, cancellationToken).ConfigureAwait(false);
            using var loadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            loadTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await session.LoadAsync(stream, loadTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MpvSourceException("Timed out waiting for mpv to load the audio stream.", ex);
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public static string? LocateMpvBinary()
    {
        if (MediaToolPaths.FindPackaged("mpv") is { } packaged) return packaged;
        if (OperatingSystem.IsLinux() && MediaToolPaths.FindPackaged("yt-dlp") is not null &&
            File.Exists("/usr/bin/mpv"))
            return "/usr/bin/mpv";

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
        private readonly TaskCompletionSource<bool> _fileLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _exited;

        public MpvPlayerSession(Process process, MpvIpcClient ipc, string endpoint, bool windows)
        {
            _process = process;
            _ipc = ipc;
            _endpoint = endpoint;
            _windows = windows;
            _ipc.EventReceived += OnEvent;
            _ipc.Disconnected += OnDisconnected;
            _process.Exited += OnProcessExited;
            _process.EnableRaisingEvents = true;
        }

        public bool IsAlive => _disposed == 0 && _exited == 0 && !_process.HasExited;
        public double TimePos => _timePos;
        public double? Duration { get; private set; }

        public event Action? EndOfFile;
        public event Action? Exited;

        public async Task LoadAsync(ResolvedAudioStream stream, CancellationToken cancellationToken)
        {
            if (stream.RequestHeaders.Count > 0)
            {
                var headers = stream.RequestHeaders.Select(header => $"{header.Key}: {header.Value}").ToArray();
                await _ipc.SendAsync(["set_property", "http-header-fields", headers], cancellationToken)
                    .ConfigureAwait(false);
            }
            await _ipc.SendAsync(["loadfile", stream.Url.AbsoluteUri], cancellationToken).ConfigureAwait(false);
            if (!await _fileLoaded.Task.WaitAsync(cancellationToken).ConfigureAwait(false))
                throw new MpvSourceException("mpv could not open the audio stream.");
            if (await _ipc.RequestAsync(["get_property", "aid"], cancellationToken).ConfigureAwait(false)
                is not { ValueKind: JsonValueKind.Number })
                throw new MpvSourceException("mpv did not select an audio track.");
            try
            {
                if (await _ipc.RequestAsync(["get_property", "duration"], cancellationToken).ConfigureAwait(false)
                    is { ValueKind: JsonValueKind.Number } duration)
                    Duration = duration.GetDouble();
            }
            catch (IOException)
            {
                // Some inputs have no duration. Keep the caller's metadata fallback.
            }
        }

        public void SetPause(bool paused) =>
            _ = FireAndForget(["set_property", "pause", paused]);

        public void SeekAbsolute(double seconds)
        {
            _timePos = Math.Max(0, seconds);
            _ = FireAndForget(["seek", seconds, "absolute+exact"]);
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
            if (name == "file-loaded")
            {
                _fileLoaded.TrySetResult(true);
                return;
            }
            if (name != "end-file") return;
            _fileLoaded.TrySetResult(false);
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            if (reason == "error")
            {
                OnDisconnected();
                return;
            }
            if (reason != "eof") return;
            if (Interlocked.Exchange(ref _eofRaised, 1) != 0) return;
            try { EndOfFile?.Invoke(); } catch { /* ignore */ }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            // Process raises Exited under its own lock. Listeners can dispose this session.
            ThreadPool.QueueUserWorkItem(static session => session.OnDisconnected(), this, preferLocal: false);
        }

        private void OnDisconnected()
        {
            _fileLoaded.TrySetResult(false);
            if (Interlocked.Exchange(ref _exited, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
            try { Exited?.Invoke(); } catch { /* ignore */ }
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
            _process.Exited -= OnProcessExited;
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

internal sealed class MpvSourceException(string message, Exception? inner = null) : IOException(message, inner);
