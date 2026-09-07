using Muses.Core.Domain;

namespace Muses.Core.Platform;

public interface IPlatformSMTC
{
    bool IsSupported { get; }
    void UpdateTrack(TrackSnapshot? track, bool isPlaying);
    void UpdatePosition(double positionSeconds, double durationSeconds);

    event Action? PlayRequested;
    event Action? PauseRequested;
    event Action? NextRequested;
    event Action? PreviousRequested;
}
