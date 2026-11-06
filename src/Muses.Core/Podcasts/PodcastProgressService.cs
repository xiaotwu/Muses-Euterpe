using System.Data.Common;
using System.Diagnostics;
using Muses.Core.Domain;

namespace Muses.Core.Podcasts;

public sealed class PodcastProgressService(IPodcastRepository repository)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProgressWrite> _pending = new(StringComparer.Ordinal);

    public event Action? Changed;

    public bool PersistenceFailed
    {
        get
        {
            lock (_gate) return _pending.Count > 0;
        }
    }

    public double? GetResumePositionMs(string videoId)
    {
        lock (_gate)
        {
            var hasPending = _pending.TryGetValue(videoId, out var pending);
            if (pending.Completed) return 0;
            try
            {
                var state = repository.Episode(videoId);
                if (state?.Completed == true) return 0;
                return hasPending ? pending.PositionMs : state?.LastPositionMs;
            }
            catch (DbException ex)
            {
                Trace.TraceWarning("Could not read podcast progress: {0}", ex.Message);
                return hasPending ? pending.PositionMs : null;
            }
        }
    }

    public void Checkpoint(TrackSnapshot track, double positionSeconds, double durationSeconds)
    {
        var positionMs = positionSeconds * 1000;
        if (double.IsFinite(positionMs) && positionMs >= 0)
            Save(track, positionMs, durationSeconds, completed: false);
    }

    public void Complete(TrackSnapshot track, double positionSeconds, double durationSeconds)
    {
        var positionMs = positionSeconds * 1000;
        Save(track, double.IsFinite(positionMs) && positionMs >= 0 ? positionMs : 0,
            durationSeconds, completed: true);
    }

    public void SetPlayed(string videoId, bool played)
    {
        bool changed;
        lock (_gate)
        {
            changed = repository.SetEpisodePlayed(videoId, played, DateTimeOffset.UtcNow);
            changed |= _pending.Remove(videoId);
        }
        if (changed) Changed?.Invoke();
    }

    public void RetryPending()
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var (videoId, write) in _pending.ToArray())
                changed |= TrySave(videoId, write);
        }
        if (changed) Changed?.Invoke();
    }

    private void Save(TrackSnapshot track, double positionMs, double durationSeconds, bool completed)
    {
        if (track.MediaKind != TrackMediaKind.PodcastEpisode) return;
        var durationMs = DurationMs(durationSeconds) ?? DurationMs(track.DurationSeconds);
        var write = new ProgressWrite(positionMs, durationMs, completed, DateTimeOffset.UtcNow);
        bool changed;
        lock (_gate)
        {
            // A later checkpoint must preserve a pending natural completion.
            if (_pending.TryGetValue(track.YouTubeId, out var pending) && pending.Completed)
                write = pending;
            changed = TrySave(track.YouTubeId, write);
        }
        if (changed) Changed?.Invoke();
    }

    private bool TrySave(string videoId, ProgressWrite write)
    {
        try
        {
            var changed = repository.SaveEpisodeProgress(videoId, write.PositionMs, write.DurationMs,
                write.Completed, write.ObservedAt);
            return _pending.Remove(videoId) || changed;
        }
        catch (DbException ex)
        {
            Trace.TraceWarning("Could not save podcast progress: {0}", ex.Message);
            var added = !_pending.ContainsKey(videoId);
            _pending[videoId] = write;
            return added;
        }
    }

    private static int? DurationMs(double seconds) =>
        seconds is >= 0.001 and <= int.MaxValue / 1000d ? (int)(seconds * 1000) : null;

    private readonly record struct ProgressWrite(
        double PositionMs, int? DurationMs, bool Completed, DateTimeOffset ObservedAt);
}
