namespace Muses.Core.Advanced;

public sealed record TrackNoteEntity(
    string Id,
    string TrackId,
    string Content,
    long CreatedAt,
    long UpdatedAt);

public sealed record TrackBookmarkEntity(
    string Id,
    string TrackId,
    double TimestampMs,
    string? Title,
    string? Note,
    long CreatedAt)
{
    public string FormattedTimestamp
    {
        get
        {
            var totalSec = (int)(TimestampMs / 1000.0);
            return $"{totalSec / 60}:{totalSec % 60:D2}";
        }
    }
}

public sealed record NoteSearchHit(
    string Id,
    string TrackId,
    string TrackTitle,
    string Snippet);

public static class TrackBookmarkExtensions
{
    public static string GetFormattedTimestamp(this TrackBookmarkEntity bm)
    {
        var totalSec = (int)(bm.TimestampMs / 1000.0);
        return $"{totalSec / 60}:{totalSec % 60:D2}";
    }
}
