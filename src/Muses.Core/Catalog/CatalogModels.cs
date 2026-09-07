using Muses.Core.Domain;

namespace Muses.Core.Catalog;

public enum CatalogReleaseKind
{
    Album,
    Single,
    Ep,
    Unknown
}

public enum CatalogCacheState
{
    Fresh,
    Stale,
    Unavailable
}

public static class CatalogCacheStateResolver
{
    public static CatalogCacheState Resolve(
        DateTimeOffset refreshedAt,
        bool unavailable,
        DateTimeOffset? now = null,
        TimeSpan? staleAfter = null)
    {
        if (unavailable) return CatalogCacheState.Unavailable;
        var current = now ?? DateTimeOffset.UtcNow;
        var maxAge = staleAfter ?? TimeSpan.FromDays(7);
        return (current - refreshedAt) > maxAge ? CatalogCacheState.Stale : CatalogCacheState.Fresh;
    }
}

public sealed record CatalogReleaseProjection(
    string StableId,
    string Title,
    string ArtistName,
    string? ArtistStableId,
    string? ArtworkUrl,
    int? Year,
    CatalogReleaseKind Kind,
    CatalogCacheState CacheState,
    IReadOnlyList<TrackSnapshot> Tracks
)
{
    public string Id => StableId;
}

public sealed record CatalogArtistProjection(
    string StableId,
    string Name,
    string? ArtworkUrl,
    string? Biography,
    CatalogCacheState CacheState,
    IReadOnlyList<CatalogReleaseProjection> Releases,
    IReadOnlyList<TrackSnapshot> Tracks
)
{
    public string Id => StableId;
}

public sealed record OnlineReleaseItem(
    string PlaylistId,
    string Title,
    string? ArtworkUrl,
    int? Year,
    CatalogReleaseKind Kind,
    string? ChannelId
)
{
    public string StableId => $"playlist:{PlaylistId}";
}

public sealed class ArtistOnlineDiscography
{
    public string ArtistName { get; }
    public string? ChannelId { get; }
    public IReadOnlyList<TrackSnapshot> TopTracks { get; }
    public IReadOnlyList<OnlineReleaseItem> Albums { get; }
    public IReadOnlyList<OnlineReleaseItem> SinglesAndEPs { get; }

    public ArtistOnlineDiscography(
        string artistName,
        string? channelId = null,
        IReadOnlyList<TrackSnapshot>? topTracks = null,
        IReadOnlyList<OnlineReleaseItem>? albums = null,
        IReadOnlyList<OnlineReleaseItem>? singlesAndEPs = null)
    {
        ArtistName = artistName;
        ChannelId = channelId;
        TopTracks = topTracks ?? [];
        Albums = albums ?? [];
        SinglesAndEPs = singlesAndEPs ?? [];
    }
}

public static class YouTubeCatalogIdentity
{
    public static string? Release(string? browseId, string? playlistId)
    {
        if (Normalized(browseId) is { } b) return $"browse:{b}";
        if (Normalized(playlistId) is { } p) return $"playlist:{p}";
        return null;
    }

    public static string? Artist(string? channelId, string? browseId)
    {
        if (Normalized(channelId) is { } c) return $"channel:{c}";
        if (Normalized(browseId) is { } b) return $"browse:{b}";
        return null;
    }

    public static bool ValidStableId(string value, IReadOnlyList<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length)
                return true;
        }
        return false;
    }

    private static string? Normalized(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

public static class YouTubePlaylistID
{
    public static bool IsMusicAlbum(string playlistId) =>
        playlistId.StartsWith("OLAK", StringComparison.Ordinal);
}
