using Muses.Core.History;
using Muses.Infrastructure.History;
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
}
