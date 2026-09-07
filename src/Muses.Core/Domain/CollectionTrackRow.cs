using Muses.Core.Library;
using Muses.Core.YouTube;

namespace Muses.Core.Domain;

/// <summary>
/// Immutable presentation value used by collection hero decks and track tables.
/// The canonical index belongs to the collection; visual table sorting never mutates it.
/// </summary>
public sealed record CollectionTrackRow
{
    public TrackSnapshot Snapshot { get; init; }
    public int CanonicalIndex { get; init; }
    public int? Year { get; init; }
    public string? Genre { get; init; }
    public DateTimeOffset? AddedAt { get; init; }
    public int PlayCount { get; init; }
    public int? TrackNumber { get; init; }
    public int? DiscNumber { get; init; }
    /// <summary>
    /// Collection occurrence identity. A playlist may contain the same Track
    /// more than once, so row identity cannot always equal media identity.
    /// </summary>
    public Guid? CollectionItemId { get; init; }

    public Guid Id => CollectionItemId ?? Snapshot.Id;
    public string Title => Snapshot.Title;
    public string Artist => Snapshot.Artist;
    public string Album => Snapshot.AlbumTitle ?? "";
    public double Duration => Snapshot.DurationSeconds;

    public int YearSortValue => Year ?? 0;
    public string GenreSortValue => Genre ?? "";
    public DateTimeOffset AddedAtSortValue => AddedAt ?? DateTimeOffset.MinValue;

    public CollectionTrackRow(
        TrackSnapshot snapshot,
        int canonicalIndex,
        int? year = null,
        string? genre = null,
        DateTimeOffset? addedAt = null,
        int playCount = 0,
        int? trackNumber = null,
        int? discNumber = null,
        Guid? collectionItemId = null)
    {
        Snapshot = snapshot;
        CanonicalIndex = canonicalIndex;
        Year = year;
        Genre = genre;
        AddedAt = addedAt;
        PlayCount = playCount;
        TrackNumber = trackNumber;
        DiscNumber = discNumber;
        CollectionItemId = collectionItemId;
    }

    public CollectionTrackRow(TrackEntity track, int canonicalIndex, Guid? collectionItemId = null)
        : this(
            track.ToSnapshot(),
            canonicalIndex,
            track.Year,
            track.Genre,
            track.AddedAt,
            track.PlayCount,
            track.TrackNo,
            track.DiscNo,
            collectionItemId)
    {
    }

    public bool Matches(TrackSnapshot? currentTrack)
    {
        if (currentTrack is null) return false;
        return currentTrack.Id == Snapshot.Id
               || (!string.IsNullOrEmpty(currentTrack.YouTubeId)
                   && currentTrack.YouTubeId == Snapshot.YouTubeId);
    }

    /// <summary>
    /// Songs is an implicit collection whose canonical order is title A-Z.
    /// Artist, album, and stable ID break equal-title ties deterministically.
    /// </summary>
    public static IReadOnlyList<CollectionTrackRow> Songs(IEnumerable<TrackEntity> tracks)
    {
        return tracks
            .Where(t => !string.IsNullOrEmpty(t.YouTubeId))
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.AlbumTitle ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Id)
            .Select((t, index) => new CollectionTrackRow(t, index))
            .ToList();
    }

    /// <summary>
    /// User playlists keep the explicit persisted PlaylistItem order.
    /// </summary>
    public static IReadOnlyList<CollectionTrackRow> Playlist(
        IEnumerable<PlaylistItemEntity> items,
        Func<Guid, TrackEntity?>? trackLookup = null)
    {
        return items
            .OrderBy(i => i.ItemOrder)
            .ThenBy(i => i.Id)
            .Select(i =>
            {
                var track = i.Track ?? (i.TrackId.HasValue && trackLookup != null ? trackLookup(i.TrackId.Value) : null);
                if (track is null || string.IsNullOrEmpty(track.YouTubeId)) return null;
                return new CollectionTrackRow(track, i.ItemOrder, i.Id);
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

    /// <summary>
    /// YouTube imports keep the explicit persisted YouTubeImportItem order.
    /// </summary>
    public static IReadOnlyList<CollectionTrackRow> YouTubeImport(
        IEnumerable<YouTubeImportItemEntity> items,
        Func<Guid, TrackEntity?>? trackLookup = null)
    {
        return items
            .OrderBy(i => i.ItemOrder)
            .ThenBy(i => i.Id)
            .Select(i =>
            {
                var track = i.Track ?? (i.TrackId.HasValue && trackLookup != null ? trackLookup(i.TrackId.Value) : null);
                if (track is not null && !string.IsNullOrEmpty(track.YouTubeId))
                {
                    return new CollectionTrackRow(track, i.ItemOrder, i.Id);
                }
                if (string.IsNullOrEmpty(i.YouTubeId)) return null;
                var snap = new TrackSnapshot(
                    i.TrackId ?? i.Id,
                    string.IsNullOrEmpty(i.Title) ? i.YouTubeId : i.Title,
                    "",
                    null,
                    0,
                    i.YouTubeId,
                    YouTubeLinks.ThumbnailUrl(i.YouTubeId));
                return new CollectionTrackRow(snap, i.ItemOrder, collectionItemId: i.Id);
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }
}

public enum CollectionTableDefaultSort
{
    TitleAZ,
    PlaylistOrder
}

public enum CollectionColumn
{
    Order,
    Title,
    Artist,
    Album,
    Year,
    Genre,
    Duration,
    AddedAt,
    Plays
}

public static class CollectionTrackSort
{
    public static IReadOnlyList<CollectionTrackRow> Rows(
        IReadOnlyList<CollectionTrackRow> rows,
        CollectionTableDefaultSort defaultSort)
    {
        return defaultSort switch
        {
            CollectionTableDefaultSort.TitleAZ => rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            CollectionTableDefaultSort.PlaylistOrder => rows.OrderBy(r => r.CanonicalIndex).ToList(),
            _ => rows
        };
    }

    public static IReadOnlyList<CollectionTrackRow> Rows(
        IReadOnlyList<CollectionTrackRow> rows,
        CollectionColumn column,
        bool ascending = true)
    {
        return column switch
        {
            CollectionColumn.Order => ascending ? rows.OrderBy(r => r.CanonicalIndex).ToList() : rows.OrderByDescending(r => r.CanonicalIndex).ToList(),
            CollectionColumn.Title => ascending ? rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList() : rows.OrderByDescending(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            CollectionColumn.Artist => ascending ? rows.OrderBy(r => r.Artist, StringComparer.CurrentCultureIgnoreCase).ToList() : rows.OrderByDescending(r => r.Artist, StringComparer.CurrentCultureIgnoreCase).ToList(),
            CollectionColumn.Album => ascending ? rows.OrderBy(r => r.Album, StringComparer.CurrentCultureIgnoreCase).ToList() : rows.OrderByDescending(r => r.Album, StringComparer.CurrentCultureIgnoreCase).ToList(),
            CollectionColumn.Year => ascending ? rows.OrderBy(r => r.YearSortValue).ToList() : rows.OrderByDescending(r => r.YearSortValue).ToList(),
            CollectionColumn.Genre => ascending ? rows.OrderBy(r => r.GenreSortValue, StringComparer.CurrentCultureIgnoreCase).ToList() : rows.OrderByDescending(r => r.GenreSortValue, StringComparer.CurrentCultureIgnoreCase).ToList(),
            CollectionColumn.Duration => ascending ? rows.OrderBy(r => r.Duration).ToList() : rows.OrderByDescending(r => r.Duration).ToList(),
            CollectionColumn.AddedAt => ascending ? rows.OrderBy(r => r.AddedAtSortValue).ToList() : rows.OrderByDescending(r => r.AddedAtSortValue).ToList(),
            CollectionColumn.Plays => ascending ? rows.OrderBy(r => r.PlayCount).ToList() : rows.OrderByDescending(r => r.PlayCount).ToList(),
            _ => rows
        };
    }
}
