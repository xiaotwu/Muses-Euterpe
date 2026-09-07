using System.Diagnostics;
using Muses.Core.Domain;
using Muses.Infrastructure.YTDlp;

namespace Muses.Infrastructure.Playback;

/// <summary>
/// Production Wave 1 engine: yt-dlp resolves a stream URL, then a host player
/// (ffplay / mpv / afplay-after-download) actually emits audio.
/// </summary>
public sealed class ProcessStreamEngine : IPlayerEngine, IDisposable
{
    private readonly IYTDlpBridge _bridge;
    private readonly string _quality;
    private Process? _player;
    private CancellationTokenSource? _loadCts;
    private Timer? _positionTimer;
    private DateTimeOffset _startedAt;
    private double _seekOffset;

    public ProcessStreamEngine(IYTDlpBridge bridge, string quality = "bestaudio")
    {
        _bridge = bridge;
        _quality = quality;
    }

    public PlayerState State { get; } = new();
    public Action? OnCompletion { get; set; }

    public async Task LoadAsync(TrackSnapshot track, CancellationToken cancellationToken = default)
    {
        StopPlayer();
        _loadCts?.Cancel();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _loadCts.Token;

        State.Track = track;
        State.Duration = track.DurationSeconds;
        State.Position = 0;
        State.Buffering = true;
        State.Error = null;
        State.IsPlaying = false;

        try
        {
            var url = await _bridge.ResolveStreamUrlAsync(track.YouTubeId, _quality, TimeSpan.FromSeconds(25), ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            StartPlayer(url);
            State.Buffering = false;
            State.IsPlaying = true;
            _startedAt = DateTimeOffset.UtcNow;
            _seekOffset = 0;
            StartClock();
        }
        catch (OperationCanceledException)
        {
            State.Buffering = false;
        }
        catch (Exception ex)
        {
            State.Buffering = false;
            State.IsPlaying = false;
            State.Error = new PlayerError(PlayerErrorKind.NetworkError, ex.Message);
            throw;
        }
    }

    public Task PrepareAsync(TrackSnapshot track, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool PlayPrepared() => false;

    public void Play()
    {
        if (State.Track is null) return;
        State.IsPlaying = true;
        _startedAt = DateTimeOffset.UtcNow;
        StartClock();
    }

    public void Pause()
    {
        State.IsPlaying = false;
        StopClock();
        StopPlayer();
    }

    public void Toggle()
    {
        if (State.IsPlaying) Pause();
        else Play();
    }

    public void Seek(double time)
    {
        _seekOffset = Math.Clamp(time, 0, Math.Max(State.Duration, 0));
        State.Position = _seekOffset;
        _startedAt = DateTimeOffset.UtcNow;
    }

    public void SetVolume(float value) { }
    public void SetEq(IReadOnlyList<EqBand> bands) { }
    public void InstallSpectrumTap(Action<SpectrumFrame> handler) { }
    public void RemoveSpectrumTap() { }

    public void Dispose()
    {
        StopClock();
        StopPlayer();
        _loadCts?.Dispose();
    }

    private void StartPlayer(Uri url)
    {
        StopPlayer();
        var (fileName, args) = ResolvePlayer(url);
        if (fileName is null) return;
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            _player = Process.Start(psi);
        }
        catch
        {
            _player = null;
        }
    }

    private static (string? FileName, string[] Args) ResolvePlayer(Uri url)
    {
        if (FindOnPath("ffplay") is { } ffplay)
            return (ffplay, ["-nodisp", "-autoexit", "-loglevel", "quiet", url.ToString()]);
        if (FindOnPath("mpv") is { } mpv)
            return (mpv, ["--no-video", "--no-terminal", "--really-quiet", url.ToString()]);
        return (null, []);
    }

    private static string? FindOnPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists);
    }

    private void StartClock()
    {
        StopClock();
        _positionTimer = new Timer(_ =>
        {
            if (!State.IsPlaying) return;
            State.Position = _seekOffset + (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
            if (State.Duration > 0 && State.Position >= State.Duration)
            {
                State.IsPlaying = false;
                State.Position = State.Duration;
                StopClock();
                StopPlayer();
                OnCompletion?.Invoke();
            }
        }, null, 200, 200);
    }

    private void StopClock()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }

    private void StopPlayer()
    {
        try
        {
            if (_player is { HasExited: false })
                _player.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
        _player?.Dispose();
        _player = null;
    }
}
