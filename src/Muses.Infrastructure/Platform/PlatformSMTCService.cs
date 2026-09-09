using Muses.Core.Domain;
using Muses.Core.Platform;
using Muses.Core.Playback;

namespace Muses.Infrastructure.Platform;

/// <summary>
/// Legacy stub retained for binary compat. Prefer <c>Muses.Platform.Windows.WindowsSMTCService</c>.
/// Still routes Raise* → PlaybackService when constructed with one.
/// </summary>
public sealed class PlatformSMTCService : IPlatformSMTC
{
    private readonly PlaybackService? _playback;

    public bool IsSupported => false;

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
        }
    }

    public void UpdateTrack(TrackSnapshot? track, bool isPlaying) { }
    public void UpdatePosition(double positionSeconds, double durationSeconds) { }
}
