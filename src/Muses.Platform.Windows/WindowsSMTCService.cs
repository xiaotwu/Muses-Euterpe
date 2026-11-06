using Muses.Core.Domain;
using Muses.Core.Platform;
using Muses.Core.Playback;

namespace Muses.Platform.Windows;

/// <summary>Publishes playback state and routes Windows media commands to PlaybackService.</summary>
public sealed class WindowsSMTCService : IPlatformSMTC, IDisposable
{
    private readonly PlaybackService? _playback;
    private readonly Action<Action> _dispatch;
    private bool _disposed;
    private IWindowsSmtcBackend _backend;

    public bool IsSupported => !_disposed && _backend.IsAvailable;

    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public void RaisePlay() => PlayRequested?.Invoke();
    public void RaisePause() => PauseRequested?.Invoke();
    public void RaiseNext() => NextRequested?.Invoke();
    public void RaisePrevious() => PreviousRequested?.Invoke();

    public WindowsSMTCService(
        PlaybackService? playback = null,
        IWindowsSmtcBackend? backend = null,
        Action<Action>? dispatch = null)
    {
        _playback = playback;
        _dispatch = dispatch ?? (action => action());
        _backend = backend ?? NullWindowsSmtcBackend.Instance;

        PlayRequested += OnPlayRequested;
        PauseRequested += OnPauseRequested;
        NextRequested += OnNextRequested;
        PreviousRequested += OnPreviousRequested;
        SubscribeBackend();

        if (_playback is not null)
            _playback.EventBus.EventPosted += OnPlaybackEvent;
    }

    public void Start(nint windowHandle)
    {
        if (_disposed || _backend.IsAvailable || windowHandle == IntPtr.Zero)
            return;
        if (WindowsSmtcBackend.TryCreate(windowHandle) is not { } backend)
            return;

        ReleaseBackend();
        _backend = backend;
        SubscribeBackend();
        RefreshPlayback();
    }

    private void SubscribeBackend()
    {
        _backend.PlayPressed += OnPlayPressed;
        _backend.PausePressed += OnPausePressed;
        _backend.NextPressed += OnNextPressed;
        _backend.PreviousPressed += OnPreviousPressed;
        _backend.SeekRequested += OnSeekRequested;
    }

    private void ReleaseBackend()
    {
        _backend.PlayPressed -= OnPlayPressed;
        _backend.PausePressed -= OnPausePressed;
        _backend.NextPressed -= OnNextPressed;
        _backend.PreviousPressed -= OnPreviousPressed;
        _backend.SeekRequested -= OnSeekRequested;
        _backend.Dispose();
    }

    private void OnPlayPressed() => Dispatch(RaisePlay);
    private void OnPausePressed() => Dispatch(RaisePause);
    private void OnNextPressed() => Dispatch(RaiseNext);
    private void OnPreviousPressed() => Dispatch(RaisePrevious);
    private void OnSeekRequested(double seconds) => Dispatch(() =>
    {
        if (_playback?.State is { Track: not null, Buffering: false, Error: null } state &&
            double.IsFinite(seconds) && state.Duration > 0)
            _playback.Seek(Math.Clamp(seconds, 0, state.Duration));
    });

    private void OnPlayRequested() => _playback?.Play();
    private void OnPauseRequested() => _playback?.Pause();
    private void OnNextRequested() => _playback?.Next();
    private void OnPreviousRequested() => _playback?.Previous();

    private void Dispatch(Action action) => _dispatch(() =>
    {
        if (!_disposed)
            action();
    });

    private void OnPlaybackEvent(PlaybackEvent evt)
    {
        if (IsSupported)
            Dispatch(RefreshPlayback);
    }

    private void RefreshPlayback()
    {
        if (_playback is null) return;
        var state = _playback.State;
        UpdateTrack(state.Track, state.IsPlaying);
        UpdatePosition(state.Position, state.Duration > 0 ? state.Duration : state.Track?.DurationSeconds ?? 0);
    }

    public void UpdateTrack(TrackSnapshot? track, bool isPlaying)
    {
        if (IsSupported)
            _backend.UpdateDisplay(track?.Title, track?.Artist, track?.ArtworkUrl, isPlaying);
    }

    public void UpdatePosition(double positionSeconds, double durationSeconds)
    {
        if (IsSupported)
            _backend.UpdateTimeline(positionSeconds, durationSeconds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_playback is not null)
            _playback.EventBus.EventPosted -= OnPlaybackEvent;
        ReleaseBackend();
    }
}

public interface IWindowsSmtcBackend : IDisposable
{
    bool IsAvailable { get; }
    event Action? PlayPressed;
    event Action? PausePressed;
    event Action? NextPressed;
    event Action? PreviousPressed;
    event Action<double>? SeekRequested;
    void UpdateDisplay(string? title, string? artist, string? artworkUrl, bool isPlaying);
    void UpdateTimeline(double positionSeconds, double durationSeconds);
}

public sealed class NullWindowsSmtcBackend : IWindowsSmtcBackend
{
    public static NullWindowsSmtcBackend Instance { get; } = new();
    public bool IsAvailable => false;
    public event Action? PlayPressed { add { } remove { } }
    public event Action? PausePressed { add { } remove { } }
    public event Action? NextPressed { add { } remove { } }
    public event Action? PreviousPressed { add { } remove { } }
    public event Action<double>? SeekRequested { add { } remove { } }
    public void UpdateDisplay(string? title, string? artist, string? artworkUrl, bool isPlaying) { }
    public void UpdateTimeline(double positionSeconds, double durationSeconds) { }
    public void Dispose() { }
}
