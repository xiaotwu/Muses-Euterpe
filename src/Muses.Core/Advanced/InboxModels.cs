namespace Muses.Core.Advanced;

public enum InboxState
{
    Unheard,
    Listening,
    Accepted,
    Rejected,
    Snoozed
}

public enum InboxSource
{
    Manual,
    Automation,
    YouTubeImport
}

public sealed record InboxItemEntity(
    string Id,
    string TrackId,
    string TrackTitle,
    string Artist,
    string? AlbumTitle,
    double DurationSeconds,
    string YouTubeId,
    string? ArtworkUrl,
    long AddedAt,
    InboxSource Source,
    InboxState State,
    long? SnoozeUntil = null,
    double? ListenedMs = null,
    string? Notes = null);
