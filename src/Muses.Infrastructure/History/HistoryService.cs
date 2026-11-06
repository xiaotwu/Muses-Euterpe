using Muses.Core.Domain;
using Muses.Core.History;
using Muses.Core.Library;
using Muses.Core.Playback;

namespace Muses.Infrastructure.History;

public sealed class HistoryService
{
    private readonly IHistoryRepository _repo;
    private readonly LibraryService? _library;
    private readonly TimeProvider _timeProvider;

    private TrackSnapshot? _activeTrack;
    private long _activeStartEpoch;
    private double _activeListenedSeconds;
    private long? _playingSince;

    public event Action? Changed;

    public HistoryService(IHistoryRepository repo, LibraryService? library = null, PlaybackService? playback = null,
        TimeProvider? timeProvider = null)
    {
        _repo = repo;
        _library = library;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (playback is not null)
        {
            playback.EventBus.EventPosted += OnPlaybackEvent;
        }
    }

    private void OnPlaybackEvent(PlaybackEvent evt)
    {
        switch (evt.Kind)
        {
            case PlaybackEventKind.TrackStarted when evt.Track is not null:
                FinalizeActiveSession(ListeningOutcome.Interrupted);
                _activeTrack = evt.Track;
                _activeStartEpoch = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
                _playingSince = _timeProvider.GetTimestamp();
                break;

            case PlaybackEventKind.TrackPaused when MatchesActiveTrack(evt):
                PauseClock();
                break;

            case PlaybackEventKind.TrackResumed when MatchesActiveTrack(evt):
                _playingSince ??= _timeProvider.GetTimestamp();
                break;

            case PlaybackEventKind.TrackCompleted when MatchesActiveTrack(evt):
                FinalizeActiveSession(ListeningOutcome.Completed);
                break;

            case PlaybackEventKind.TrackSkipped when MatchesActiveTrack(evt):
                FinalizeActiveSession(ListeningOutcome.Skipped);
                break;

            case PlaybackEventKind.TrackStopped when MatchesActiveTrack(evt):
                FinalizeActiveSession(ListeningOutcome.Interrupted);
                break;
        }
    }

    private bool MatchesActiveTrack(PlaybackEvent evt) => _activeTrack is { } active && evt.Track?.Id == active.Id;

    private void PauseClock()
    {
        if (_playingSince is not { } since) return;
        _activeListenedSeconds += _timeProvider.GetElapsedTime(since).TotalSeconds;
        _playingSince = null;
    }

    private void FinalizeActiveSession(ListeningOutcome outcome)
    {
        if (_activeTrack is null) return;

        PauseClock();
        var track = _activeTrack;
        var listenedMs = (int)Math.Clamp(_activeListenedSeconds * 1000, 0, int.MaxValue);
        _activeTrack = null;
        _activeListenedSeconds = 0;
        if (listenedMs >= 2000 || outcome == ListeningOutcome.Completed)
        {
            var evt = new ListeningEventEntity(
                Id: Guid.NewGuid().ToString(),
                TrackId: track.Id.ToString(),
                StartedAt: _activeStartEpoch,
                EndedAt: _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                Outcome: outcome,
                ContextSummaryJson: null,
                ListenedMs: listenedMs
            );
            _repo.RecordEvent(evt);

            Changed?.Invoke();
        }

    }

    public void Clear()
    {
        _repo.ClearHistory();
        Changed?.Invoke();
    }

    public ListeningHistoryDashboard GetDashboard(RecapRange range)
    {
        var now = _timeProvider.GetUtcNow();
        long? fromEpoch = range switch
        {
            RecapRange.Week => now.AddDays(-7).ToUnixTimeSeconds(),
            RecapRange.Month => now.AddDays(-30).ToUnixTimeSeconds(),
            RecapRange.Year => now.AddDays(-365).ToUnixTimeSeconds(),
            RecapRange.AllTime => null,
            _ => null
        };

        var events = _repo.GetEvents(fromStartedAt: fromEpoch);
        var totalEvents = events.Count;
        long totalMs = 0;
        var completedCount = 0;
        var skippedCount = 0;

        var trackTallyMap = new Dictionary<string, (int Count, int Ms)>();
        var artistTallyMap = new Dictionary<string, (int Count, int Ms)>();

        foreach (var evt in events)
        {
            totalMs += evt.ListenedMs;
            if (evt.Outcome == ListeningOutcome.Completed) completedCount++;
            if (evt.Outcome == ListeningOutcome.Skipped) skippedCount++;

            if (!string.IsNullOrEmpty(evt.TrackId))
            {
                var tr = _library?.Get(Guid.TryParse(evt.TrackId, out var trGuid) ? trGuid : Guid.Empty);
                var title = tr?.Title ?? "Unknown Track";
                var artist = tr?.Artist ?? "Unknown Artist";

                if (!trackTallyMap.TryGetValue(evt.TrackId, out var trData)) trData = (0, 0);
                trackTallyMap[evt.TrackId] = (trData.Count + 1, trData.Ms + evt.ListenedMs);

                if (!artistTallyMap.TryGetValue(artist, out var artData)) artData = (0, 0);
                artistTallyMap[artist] = (artData.Count + 1, artData.Ms + evt.ListenedMs);
            }
        }

        var topTracks = trackTallyMap
            .OrderByDescending(kvp => kvp.Value.Count)
            .ThenByDescending(kvp => kvp.Value.Ms)
            .Take(10)
            .Select(kvp =>
            {
                var tr = _library?.Get(Guid.TryParse(kvp.Key, out var g) ? g : Guid.Empty);
                return new TrackTally(
                    TrackId: kvp.Key,
                    Title: tr?.Title ?? "Unknown Track",
                    Artist: tr?.Artist ?? "Unknown Artist",
                    ArtworkUrl: tr?.ArtworkUrl,
                    Count: kvp.Value.Count,
                    TotalMs: kvp.Value.Ms
                );
            })
            .ToList();

        var topArtists = artistTallyMap
            .OrderByDescending(kvp => kvp.Value.Count)
            .ThenByDescending(kvp => kvp.Value.Ms)
            .Take(10)
            .Select(kvp => new ArtistTally(kvp.Key, kvp.Value.Count, kvp.Value.Ms))
            .ToList();

        var heatmap = BuildHeatmap(events, range);

        return new ListeningHistoryDashboard(
            TotalEventCount: totalEvents,
            TotalListenedMs: totalMs,
            CompletedCount: completedCount,
            SkippedCount: skippedCount,
            UniqueTracks: trackTallyMap.Count,
            UniqueArtists: artistTallyMap.Count,
            TopTracks: topTracks,
            TopArtists: topArtists,
            Heatmap: heatmap,
            RecentEvents: events.Take(20).ToList()
        );
    }

    private static ListeningHeatmap BuildHeatmap(IReadOnlyList<ListeningEventEntity> events, RecapRange range)
    {
        // 7 weekday rows: Monday (0) to Sunday (6)
        var weekdayLabels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var cellAccumulator = new Dictionary<(int Row, int Hour), (int TotalMs, int Count)>();

        foreach (var evt in events)
        {
            var localTime = DateTimeOffset.FromUnixTimeSeconds(evt.StartedAt).ToLocalTime();
            // In .NET, DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
            var rowIdx = ((int)localTime.DayOfWeek + 6) % 7; // Monday=0, ..., Sunday=6
            var hour = localTime.Hour;

            var key = (rowIdx, hour);
            if (!cellAccumulator.TryGetValue(key, out var acc)) acc = (0, 0);
            cellAccumulator[key] = (acc.TotalMs + evt.ListenedMs, acc.Count + 1);
        }

        var rows = new List<ListeningHeatmapRow>();
        var allCells = new List<ListeningHeatmapCell>();
        var totalHeatmapMs = 0;

        for (var r = 0; r < 7; r++)
        {
            var cells = new List<ListeningHeatmapCell>();
            for (var h = 0; h < 24; h++)
            {
                var hasData = cellAccumulator.TryGetValue((r, h), out var val);
                var ms = hasData ? val.TotalMs : 0;
                var count = hasData ? val.Count : 0;
                totalHeatmapMs += ms;

                var level = HeatmapLevelResolver.FromMilliseconds(ms);
                var cell = new ListeningHeatmapCell(
                    Id: $"{r}-{h}",
                    RowId: r.ToString(),
                    RowIndex: r,
                    Hour: h,
                    TotalMs: ms,
                    IntensityMs: ms,
                    EventCount: count,
                    TrackCount: count > 0 ? 1 : 0,
                    ArtistCount: count > 0 ? 1 : 0,
                    Level: level
                );
                cells.Add(cell);
                allCells.Add(cell);
            }
            rows.Add(new ListeningHeatmapRow(r.ToString(), r, weekdayLabels[r], cells));
        }

        var peak = allCells.Where(c => c.TotalMs > 0).OrderByDescending(c => c.TotalMs).FirstOrDefault();
        return new ListeningHeatmap(range, rows, totalHeatmapMs, peak);
    }
}
