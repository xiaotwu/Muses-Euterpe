using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Muses.Core.Discovery;
using Muses.Core.Domain;

namespace Muses.Infrastructure.Discovery;

public sealed class HomeFeedCache
{
    public enum Layer
    {
        Baseline,
        Web
    }

    public sealed record CachedSnapshot(HomeSnapshot Value, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt);

    public static readonly TimeSpan BaselineFreshWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan WebFreshWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan WebStaleLimit = TimeSpan.FromDays(7);

    private readonly string _directory;
    private readonly TimeSpan _baselineFreshWindow;
    private readonly TimeSpan _webFreshWindow;
    private readonly TimeSpan _webStaleLimit;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HomeFeedCache(
        string? directory = null,
        TimeSpan? baselineFreshWindow = null,
        TimeSpan? webFreshWindow = null,
        TimeSpan? webStaleLimit = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Muses", "Cache", "home-feed");
        _baselineFreshWindow = baselineFreshWindow ?? BaselineFreshWindow;
        _webFreshWindow = webFreshWindow ?? WebFreshWindow;
        _webStaleLimit = webStaleLimit ?? WebStaleLimit;
    }

    public static string Key(HomeDiscoveryInput input)
    {
        var top = string.Join(",", input.TopArtistNames.Take(3));
        var liked = string.Join(",", input.LikedArtistNames.Take(2));
        return $"feed|band={input.TimeBand}|top={top}|liked={liked}";
    }

    public CachedSnapshot? Get(HomeDiscoveryInput input, Layer layer, DateTimeOffset? now = null)
    {
        if (layer == Layer.Web && input.Scope is HomeFeedScope.Guest) return null;

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var filePath = GetFilePath(input.Scope, layer, Key(input));
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var dto = JsonSerializer.Deserialize<SnapshotDto>(json, JsonOptions);
            if (dto is null) return null;

            var snapshot = dto.ToDomain();
            if (!BelongsToScope(snapshot, input.Scope) || !IsValidForLayer(snapshot, layer))
            {
                return null;
            }

            if (layer == Layer.Web && (currentTime - snapshot.FetchedAt) > _webStaleLimit)
            {
                return null;
            }

            return new CachedSnapshot(snapshot, snapshot.FetchedAt, snapshot.ExpiresAt);
        }
        catch
        {
            return null;
        }
    }

    public bool IsFresh(CachedSnapshot cached, Layer layer, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var window = layer == Layer.Baseline ? _baselineFreshWindow : _webFreshWindow;
        return (currentTime - cached.FetchedAt) <= window && cached.ExpiresAt > currentTime;
    }

    public bool Set(HomeSnapshot snapshot, HomeDiscoveryInput input, Layer layer)
    {
        if (!BelongsToScope(snapshot, input.Scope) || !IsValidForLayer(snapshot, layer))
            return false;
        if (layer == Layer.Web && input.Scope is HomeFeedScope.Guest)
            return false;

        try
        {
            var filePath = GetFilePath(input.Scope, layer, Key(input));
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dto = SnapshotDto.FromDomain(snapshot);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Invalidate(HomeFeedScope? scope = null, Layer? layer = null)
    {
        try
        {
            if (scope is null && layer is null)
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, true);
                return;
            }

            var scopeNamespace = scope?.CacheNamespace;
            var targetDir = scopeNamespace is not null
                ? Path.Combine(_directory, scopeNamespace)
                : _directory;

            if (layer is not null && scopeNamespace is not null)
            {
                var layerDir = Path.Combine(targetDir, layer.Value.ToString().ToLowerInvariant());
                if (Directory.Exists(layerDir))
                    Directory.Delete(layerDir, true);
            }
            else if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
        catch
        {
            // Best effort cache invalidation
        }
    }

    private string GetFilePath(HomeFeedScope scope, Layer layer, string key)
    {
        var layerName = layer.ToString().ToLowerInvariant();
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_directory, scope.CacheNamespace, layerName, $"{keyHash}.json");
    }

    private static bool BelongsToScope(HomeSnapshot snapshot, HomeFeedScope expectedScope)
    {
        return (snapshot.Scope, expectedScope) switch
        {
            (HomeFeedScope.Guest, HomeFeedScope.Guest) => true,
            (HomeFeedScope.Account a1, HomeFeedScope.Account a2) => string.Equals(a1.ChannelId, a2.ChannelId, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsValidForLayer(HomeSnapshot snapshot, Layer layer)
    {
        return layer switch
        {
            Layer.Baseline => snapshot.Sections.All(s => s.Source != HomeSource.SignedInWeb),
            Layer.Web => snapshot.Scope is HomeFeedScope.Account && snapshot.Sections.Count > 0 &&
                         snapshot.Sections.All(s => s.Source == HomeSource.SignedInWeb),
            _ => true
        };
    }

    #region DTOs

    private sealed class SnapshotDto
    {
        public string ScopeKind { get; set; } = "guest";
        public string? AccountChannelId { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? StaleReason { get; set; }
        public List<SectionDto> Sections { get; set; } = [];

        public static SnapshotDto FromDomain(HomeSnapshot s) => new()
        {
            ScopeKind = s.Scope is HomeFeedScope.Account ? "account" : "guest",
            AccountChannelId = (s.Scope as HomeFeedScope.Account)?.ChannelId,
            FetchedAt = s.FetchedAt,
            ExpiresAt = s.ExpiresAt,
            StaleReason = s.StaleReason,
            Sections = s.Sections.Select(SectionDto.FromDomain).ToList()
        };

        public HomeSnapshot ToDomain()
        {
            HomeFeedScope scope = ScopeKind == "account" && !string.IsNullOrEmpty(AccountChannelId)
                ? new HomeFeedScope.Account(AccountChannelId)
                : new HomeFeedScope.Guest();

            return new HomeSnapshot(
                scope,
                Sections.Select(s => s.ToDomain()).ToList(),
                FetchedAt,
                ExpiresAt,
                StaleReason);
        }
    }

    private sealed class SectionDto
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public string Kind { get; set; } = "YouTubeCarousel";
        public string Source { get; set; } = "PublicDiscovery";
        public string? CachedOrigin { get; set; }
        public string? StaleReason { get; set; }
        public DateTimeOffset? FetchedAt { get; set; }
        public List<ItemDto> Items { get; set; } = [];

        public static SectionDto FromDomain(HomeSection s) => new()
        {
            Id = s.Id,
            Title = s.Title,
            Subtitle = s.Subtitle,
            Kind = s.Kind.ToString(),
            Source = s.Source.ToString(),
            CachedOrigin = s.CachedOrigin?.ToString(),
            StaleReason = s.StaleReason,
            FetchedAt = s.FetchedAt,
            Items = s.Items.Select(ItemDto.FromDomain).ToList()
        };

        public HomeSection ToDomain()
        {
            var kind = Enum.TryParse<HomeSectionKind>(Kind, true, out var k) ? k : HomeSectionKind.YouTubeCarousel;
            var source = Enum.TryParse<HomeSource>(Source, true, out var src) ? src : HomeSource.PublicDiscovery;
            HomeSource? cachedOrigin = Enum.TryParse<HomeSource>(CachedOrigin, true, out var co) ? co : null;

            return new HomeSection(
                Id,
                Title,
                Subtitle,
                kind,
                Items.Select(i => i.ToDomain()).ToList(),
                new SectionStatus.Loaded(),
                source,
                cachedOrigin,
                StaleReason,
                FetchedAt);
        }
    }

    private sealed class ItemDto
    {
        public string Type { get; set; } = "youtube";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Uploader { get; set; }
        public double? Duration { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? PlayEndpointVideoId { get; set; }

        public static ItemDto FromDomain(DiscoveryItem item) => item switch
        {
            DiscoveryItem.YouTube yt => new ItemDto
            {
                Type = "youtube",
                Id = yt.Card.Id,
                Title = yt.Card.Title,
                Uploader = yt.Card.Uploader,
                Duration = yt.Card.Duration,
                ThumbnailUrl = yt.Card.ThumbnailUrl,
                PlayEndpointVideoId = yt.Card.PlayableVideoId
            },
            DiscoveryItem.Track tr => new ItemDto
            {
                Type = "track",
                Id = tr.Snapshot.Id.ToString(),
                Title = tr.Snapshot.Title,
                Uploader = tr.Snapshot.Artist,
                Duration = tr.Snapshot.DurationSeconds,
                ThumbnailUrl = tr.Snapshot.ArtworkUrl,
                PlayEndpointVideoId = tr.Snapshot.YouTubeId
            },
            _ => new ItemDto()
        };

        public DiscoveryItem ToDomain()
        {
            if (Type == "track")
            {
                Guid.TryParse(Id, out var gid);
                var snap = new TrackSnapshot(
                    gid,
                    Title,
                    Uploader ?? "Unknown",
                    null,
                    Duration ?? 0,
                    PlayEndpointVideoId ?? "",
                    ThumbnailUrl);
                return new DiscoveryItem.Track(snap);
            }

            var card = new YouTubeDiscoveryCard(
                Id,
                Title,
                Uploader,
                Duration,
                ThumbnailUrl,
                playEndpoint: !string.IsNullOrEmpty(PlayEndpointVideoId)
                    ? new HomeCardEndpoint(HomeCardEndpointKind.Video, PlayEndpointVideoId)
                    : null);
            return new DiscoveryItem.YouTube(card);
        }
    }

    #endregion
}
