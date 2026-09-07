using Muses.Core.Domain;

namespace Muses.Core.Playback;

public enum PlaybackEventKind
{
    TrackStarted,
    TrackPaused,
    TrackResumed,
    TrackSeeked,
    TrackCompleted,
    TrackSkipped,
    TrackStopped,
    QueueChanged
}

public sealed record PlaybackEvent(
    PlaybackEventKind Kind,
    TrackSnapshot? Track,
    Guid? TrackId = null,
    double? ToMs = null);

public sealed class PlaybackEventBus
{
    public event Action<PlaybackEvent>? EventPosted;

    public void Post(PlaybackEvent evt) => EventPosted?.Invoke(evt);
}
