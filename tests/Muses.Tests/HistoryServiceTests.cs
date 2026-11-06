using Muses.Core.Domain;
using Muses.Core.History;
using Muses.Core.Library;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Infrastructure.History;
using Muses.Infrastructure.Playback;
using Muses.Persistence;
using Xunit;

namespace Muses.Tests;

public class HistoryServiceTests
{
    [Fact]
    public void RecordEvent_And_GetDashboard_CalculatesCorrectStatsAndHeatmap()
    {
        var store = SqliteStore.Open(inMemory: true);
        var history = new HistoryService(store);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        store.RecordEvent(new ListeningEventEntity(
            Id: "evt-1",
            TrackId: "tr-1",
            StartedAt: now - 3600,
            EndedAt: now - 3400,
            Outcome: ListeningOutcome.Completed,
            ContextSummaryJson: null,
            ListenedMs: 200_000
        ));

        store.RecordEvent(new ListeningEventEntity(
            Id: "evt-2",
            TrackId: "tr-1",
            StartedAt: now - 1800,
            EndedAt: now - 1700,
            Outcome: ListeningOutcome.Skipped,
            ContextSummaryJson: null,
            ListenedMs: 100_000
        ));

        var dash = history.GetDashboard(RecapRange.Week);

        Assert.Equal(2, dash.TotalEventCount);
        Assert.Equal(300_000, dash.TotalListenedMs);
        Assert.Equal(1, dash.CompletedCount);
        Assert.Equal(1, dash.SkippedCount);
        Assert.NotNull(dash.Heatmap);
        Assert.Equal(7, dash.Heatmap.Rows.Count); // 7 weekdays
        Assert.All(dash.Heatmap.Rows, row => Assert.Equal(24, row.Cells.Count)); // 24 hours
        Assert.Equal(300_000, dash.Heatmap.TotalMs);
    }

    [Fact]
    public async Task Restored_paused_track_is_counted_once_on_play_and_logged_on_completion()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var track = library.UpsertFromYouTube("abcdefghijk", "Song", "Artist", 60, null);
        var queue = new QueueService { Store = store };
        queue.Play(track, [track], QueueSource.Search);
        queue.CheckpointPosition(track.Id, 12_000);
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, queue, library);
        var history = new HistoryService(store, library, playback);

        await playback.RestoreCurrentPausedAsync();

        Assert.False(playback.State.IsPlaying);
        Assert.Equal(12, playback.State.Position);
        Assert.Equal(0, library.Get(track.Id)!.PlayCount);
        Assert.Equal(0, history.GetDashboard(RecapRange.AllTime).TotalEventCount);

        playback.Play();
        playback.Pause();
        playback.Play();
        engine.Complete();

        Assert.Equal(1, library.Get(track.Id)!.PlayCount);
        var recorded = Assert.Single(store.GetEvents());
        Assert.Equal(track.Id.ToString(), recorded.TrackId);
        Assert.Equal(ListeningOutcome.Completed, recorded.Outcome);
    }

    [Fact]
    public void Clear_RemovesAllEvents()
    {
        var store = SqliteStore.Open(inMemory: true);
        var history = new HistoryService(store);

        store.RecordEvent(new ListeningEventEntity(
            Id: "evt-1",
            TrackId: "tr-1",
            StartedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            EndedAt: null,
            Outcome: ListeningOutcome.Completed,
            ContextSummaryJson: null,
            ListenedMs: 60_000
        ));

        Assert.Equal(1, history.GetDashboard(RecapRange.AllTime).TotalEventCount);

        history.Clear();

        Assert.Equal(0, history.GetDashboard(RecapRange.AllTime).TotalEventCount);
    }

    [Fact]
    public void Skipping_and_quitting_record_listening_without_counting_pause_or_seek()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var playback = new PlaybackService(new FakePlayerEngine(), new QueueService());
        var clock = new ListeningClock();
        var history = new HistoryService(store, playback: playback, timeProvider: clock);
        var first = TrackSnapshot.Test("first", durationSeconds: 120);
        var second = TrackSnapshot.Test("second", durationSeconds: 120);

        playback.PlayTrack(first, [first, second], QueueSource.Songs);
        clock.Advance(3);
        playback.Pause();
        clock.Advance(60);
        playback.Seek(90);
        playback.Play();
        clock.Advance(4);
        playback.Next();
        clock.Advance(5);
        playback.StopForShutdown();

        var events = store.GetEvents();
        Assert.Equal(7000, Assert.Single(events, item => item.Outcome == ListeningOutcome.Skipped).ListenedMs);
        Assert.Equal(5000, Assert.Single(events, item => item.Outcome == ListeningOutcome.Interrupted).ListenedMs);
        Assert.Equal(12000, history.GetDashboard(RecapRange.AllTime).TotalListenedMs);
    }

    private sealed class ListeningClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
}
