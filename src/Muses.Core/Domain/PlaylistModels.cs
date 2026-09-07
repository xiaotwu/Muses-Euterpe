using Muses.Core.Library;

namespace Muses.Core.Domain;

public sealed class PlaylistEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Pinned { get; set; }
    public List<PlaylistItemEntity> Items { get; set; } = [];
}

public sealed class PlaylistItemEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlaylistId { get; set; }
    public Guid? TrackId { get; set; }
    public int ItemOrder { get; set; }
    public TrackEntity? Track { get; set; }
}

public sealed class YouTubeImportEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PlaylistId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "";
    public string? ArtworkUrl { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? AccountChannelId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<YouTubeImportItemEntity> Items { get; set; } = [];
}

public sealed class YouTubeImportItemEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImportId { get; set; }
    public Guid? TrackId { get; set; }
    public string YouTubeId { get; set; } = "";
    public string Title { get; set; } = "";
    public int ItemOrder { get; set; }
    public string? PlaylistItemId { get; set; }
    public TrackEntity? Track { get; set; }
}

/// <summary>
/// Complete local-only information needed to undo a playlist deletion during
/// the current session.
/// </summary>
public sealed record PlaylistDeletionSnapshot(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    bool Pinned,
    IReadOnlyList<PlaylistDeletionItemSnapshot> Items);

public sealed record PlaylistDeletionItemSnapshot(int Order, Guid? TrackId);
