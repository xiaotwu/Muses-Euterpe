namespace Muses.Core.History;

public enum ListeningOutcome
{
    Completed,
    Skipped,
    Interrupted
}

public sealed record ListeningEventEntity(
    string Id,
    string? TrackId,
    long StartedAt,
    long? EndedAt,
    ListeningOutcome Outcome,
    string? ContextSummaryJson,
    int ListenedMs = 0
);

public enum RecapRange
{
    Week,
    Month,
    Year,
    AllTime
}

public enum ListeningHeatmapLevel
{
    None = 0,
    Trace = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Peak = 5
}

public static class HeatmapLevelResolver
{
    public static ListeningHeatmapLevel FromMilliseconds(int ms)
    {
        if (ms <= 0) return ListeningHeatmapLevel.None;
        if (ms < 300_000) return ListeningHeatmapLevel.Trace;    // < 5m
        if (ms < 900_000) return ListeningHeatmapLevel.Low;      // < 15m
        if (ms < 1_800_000) return ListeningHeatmapLevel.Medium; // < 30m
        if (ms < 3_600_000) return ListeningHeatmapLevel.High;   // < 60m
        return ListeningHeatmapLevel.Peak;                       // >= 60m
    }
}

public sealed record ListeningHeatmapCell(
    string Id,
    string RowId,
    int RowIndex,
    int Hour,
    int TotalMs,
    int IntensityMs,
    int EventCount,
    int TrackCount,
    int ArtistCount,
    ListeningHeatmapLevel Level
);

public sealed record ListeningHeatmapRow(
    string Id,
    int Index,
    string Label,
    IReadOnlyList<ListeningHeatmapCell> Cells
);

public sealed record ListeningHeatmap(
    RecapRange Range,
    IReadOnlyList<ListeningHeatmapRow> Rows,
    int TotalMs,
    ListeningHeatmapCell? PeakCell
);

public sealed record TrackTally(
    string TrackId,
    string Title,
    string Artist,
    string? ArtworkUrl,
    int Count,
    int TotalMs
);

public sealed record ArtistTally(
    string Artist,
    int Count,
    int TotalMs
);

public sealed record ListeningHistoryDashboard(
    int TotalEventCount,
    long TotalListenedMs,
    int CompletedCount,
    int SkippedCount,
    int UniqueTracks,
    int UniqueArtists,
    IReadOnlyList<TrackTally> TopTracks,
    IReadOnlyList<ArtistTally> TopArtists,
    ListeningHeatmap Heatmap,
    IReadOnlyList<ListeningEventEntity> RecentEvents
);
