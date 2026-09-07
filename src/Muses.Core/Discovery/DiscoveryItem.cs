using Muses.Core.Domain;
using Muses.Core.L10n;

namespace Muses.Core.Discovery;

public enum HomeCardEndpointKind
{
    Video,
    Playlist,
    Browse,
    Channel
}

public readonly record struct HomeCardEndpoint(HomeCardEndpointKind Kind, string Identifier);

public enum HomeCardAvailability
{
    Available,
    Unavailable,
    RegionBlocked,
    PrivateItem,
    Deleted
}

public sealed class YouTubeDiscoveryCard
{
    public string Id { get; }
    public string Title { get; }
    public string? Uploader { get; }
    public double? Duration { get; }
    public string? ThumbnailUrl { get; }
    public HomeCardEndpoint? BrowseEndpoint { get; }
    public HomeCardEndpoint? PlayEndpoint { get; }
    public HomeCardAvailability Availability { get; }

    public YouTubeDiscoveryCard(string id, string title, string? uploader = null,
        double? duration = null, string? thumbnailUrl = null,
        HomeCardEndpoint? browseEndpoint = null, HomeCardEndpoint? playEndpoint = null,
        HomeCardAvailability availability = HomeCardAvailability.Available)
    {
        Id = id;
        Title = title;
        Uploader = uploader;
        Duration = duration;
        ThumbnailUrl = thumbnailUrl;
        BrowseEndpoint = browseEndpoint;
        PlayEndpoint = playEndpoint ?? new HomeCardEndpoint(HomeCardEndpointKind.Video, id);
        Availability = availability;
    }

    public string? PlayableVideoId
    {
        get
        {
            if (Availability != HomeCardAvailability.Available) return null;
            if (PlayEndpoint is { Kind: HomeCardEndpointKind.Video } play)
                return play.Identifier;
            return BrowseEndpoint is null ? Id : null;
        }
    }
}

public abstract record DiscoveryItem
{
    public abstract string Id { get; }
    public abstract string? HomeMediaIdentity { get; }

    public sealed record YouTube(YouTubeDiscoveryCard Card) : DiscoveryItem
    {
        public override string Id => $"yt:{Card.Id}";
        public override string? HomeMediaIdentity =>
            Card.PlayableVideoId is { Length: > 0 } vid ? $"video:{vid}" :
            Card.BrowseEndpoint is { } b ? $"{b.Kind}:{b.Identifier}" :
            Card.Id.Length > 0 ? $"legacy:{Card.Id}" : null;
    }

    public sealed record Track(TrackSnapshot Snapshot) : DiscoveryItem
    {
        public override string Id => $"tr:{Snapshot.Id}";
        public override string? HomeMediaIdentity =>
            !string.IsNullOrEmpty(Snapshot.YouTubeId) ? $"video:{Snapshot.YouTubeId}" : $"tr:{Snapshot.Id}";
    }
}

public enum HomeSectionKind
{
    AlbumCarousel,
    PlaylistCarousel,
    SongGrid,
    QuickPicks,
    Mixed,
    Community,
    YouTubeCarousel
}

public abstract record SectionStatus
{
    public sealed record Idle : SectionStatus;
    public sealed record Loading : SectionStatus;
    public sealed record Loaded : SectionStatus;
    public sealed record Failed(string? Message) : SectionStatus;
}

public enum HomeSource
{
    SignedInWeb,
    OfficialAccount,
    PublicDiscovery,
    LocalLibrary,
    Cached
}

public static class HomeSourceExtensions
{
    public static string Label(this HomeSource source) => source switch
    {
        HomeSource.SignedInWeb => L10n.L10n.Tr("YouTube Music personalized", "YouTube Music 个性化"),
        HomeSource.OfficialAccount => L10n.L10n.Tr("Your YouTube account", "你的 YouTube 账号"),
        HomeSource.PublicDiscovery => L10n.L10n.Tr("Public discovery", "公共发现"),
        HomeSource.LocalLibrary => L10n.L10n.Tr("Your library", "你的资料库"),
        HomeSource.Cached => L10n.L10n.Tr("Saved result", "已保存结果"),
        _ => L10n.L10n.Tr("Public discovery", "公共发现")
    };
}

public enum ListeningTimeBand
{
    Morning,
    Afternoon,
    Evening,
    LateNight
}

public abstract record HomeFeedScope
{
    public sealed record Guest : HomeFeedScope;
    public sealed record Account(string ChannelId) : HomeFeedScope;

    public string CacheNamespace => this switch
    {
        Account acc => $"account-{acc.ChannelId}",
        _ => "guest"
    };
}

public sealed class HomeDiscoveryInput
{
    public IReadOnlyList<string> TopArtistNames { get; }
    public IReadOnlyList<string> RecentlyPlayedArtistNames { get; }
    public IReadOnlyList<string> LikedArtistNames { get; }
    public ListeningTimeBand TimeBand { get; }
    public int Hour { get; }
    public IReadOnlyList<string> SeedVideoIds { get; }
    public HomeFeedScope Scope { get; }

    public HomeDiscoveryInput(
        IReadOnlyList<string> topArtistNames,
        IReadOnlyList<string> recentlyPlayedArtistNames,
        IReadOnlyList<string> likedArtistNames,
        ListeningTimeBand timeBand,
        int hour,
        IReadOnlyList<string> seedVideoIds,
        HomeFeedScope? scope = null)
    {
        TopArtistNames = topArtistNames;
        RecentlyPlayedArtistNames = recentlyPlayedArtistNames;
        LikedArtistNames = likedArtistNames;
        TimeBand = timeBand;
        Hour = hour;
        SeedVideoIds = seedVideoIds;
        Scope = scope ?? new HomeFeedScope.Guest();
    }
}

public sealed class HomeSnapshot
{
    public HomeFeedScope Scope { get; }
    public IReadOnlyList<HomeSection> Sections { get; }
    public DateTimeOffset FetchedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string? StaleReason { get; }

    public HomeSnapshot(
        HomeFeedScope scope,
        IReadOnlyList<HomeSection> sections,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt,
        string? staleReason = null)
    {
        Scope = scope;
        Sections = sections;
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
        StaleReason = staleReason;
    }
}

public sealed class HomeSection
{
    public string Id { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public HomeSectionKind Kind { get; }
    public IReadOnlyList<DiscoveryItem> Items { get; }
    public SectionStatus Status { get; set; }
    public HomeSource Source { get; }
    public HomeSource? CachedOrigin { get; }
    public string? StaleReason { get; }
    public DateTimeOffset? FetchedAt { get; }

    public HomeSection(
        string id,
        string title,
        string? subtitle,
        HomeSectionKind kind,
        IReadOnlyList<DiscoveryItem> items,
        SectionStatus? status = null,
        HomeSource source = HomeSource.PublicDiscovery,
        HomeSource? cachedOrigin = null,
        string? staleReason = null,
        DateTimeOffset? fetchedAt = null)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Kind = kind;
        Items = items;
        Status = status ?? new SectionStatus.Loaded();
        Source = source;
        CachedOrigin = cachedOrigin;
        StaleReason = staleReason;
        FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow;
    }

    public HomeSection PresentedFromCache(string? staleReason = null) => new(
        Id,
        Title,
        Subtitle,
        Kind,
        Items,
        Status,
        HomeSource.Cached,
        Source == HomeSource.Cached ? CachedOrigin : Source,
        staleReason ?? StaleReason,
        FetchedAt
    );
}

public static class TopPicksResolver
{
    public static IReadOnlyList<DiscoveryItem> Picks(
        DiscoveryItem? hero,
        IEnumerable<DiscoveryItem> mixed,
        IEnumerable<DiscoveryItem> recent,
        int max = 6)
    {
        var output = new List<DiscoveryItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(DiscoveryItem? item)
        {
            if (item is null || output.Count >= max) return;
            var key = item.HomeMediaIdentity ?? item.Id;
            if (!seen.Add(key)) return;
            output.Add(item);
        }

        Add(hero);
        foreach (var item in mixed) Add(item);
        foreach (var item in recent) Add(item);
        return output;
    }
}

public static class NewFeaturedResolver
{
    public static DiscoveryItem? Featured(IReadOnlyList<DiscoveryItem> items) =>
        items.Count == 0 ? null : items[0];
}
