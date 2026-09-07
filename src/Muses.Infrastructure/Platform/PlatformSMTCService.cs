using Muses.Core.Domain;
using Muses.Core.Platform;
using Muses.Core.Playback;

namespace Muses.Infrastructure.Platform;

public sealed class PlatformSMTCService : IPlatformSMTC
{
    private readonly PlaybackService? _playback;

    public bool IsSupported => OperatingSystem.IsWindows();

    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public void RaisePlay() => PlayRequested?.Invoke();
    public void RaisePause() => PauseRequested?.Invoke();
    public void RaiseNext() => NextRequested?.Invoke();
    public void RaisePrevious() => PreviousRequested?.Invoke();

    public PlatformSMTCService(PlaybackService? playback = null)
    {
        _playback = playback;
        if (_playback is not null)
        {
            PlayRequested += _playback.Play;
            PauseRequested += _playback.Pause;
            NextRequested += _playback.Next;
            PreviousRequested += _playback.Previous;

            _playback.EventBus.EventPosted += OnPlaybackEvent;
        }
    }

    private void OnPlaybackEvent(PlaybackEvent evt)
    {
        if (_playback is null) return;
        UpdateTrack(_playback.State.Track, _playback.State.IsPlaying);
        UpdatePosition(_playback.State.Position, _playback.State.Track?.DurationSeconds ?? 0);
    }

    public void UpdateTrack(TrackSnapshot? track, bool isPlaying)
    {
        // On Windows 10/11: updates SystemMediaTransportControls display
        // On macOS: updates MPNowPlayingInfoCenter / MPRemoteCommandCenter
    }

    public void UpdatePosition(double positionSeconds, double durationSeconds)
    {
        // Updates timeline properties
    }
}
