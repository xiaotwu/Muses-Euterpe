using Muses.Core.Discovery;
using Muses.Core.L10n;
using Muses.Core.Library;

namespace Muses.Infrastructure.Discovery;

public sealed record YouTubeHomeMood(string Id, string En, string Zh, string SearchQuery)
{
    public string LocalizedTitle => L10n.Tr(En, Zh);

    public static readonly IReadOnlyList<YouTubeHomeMood> All =
    [
        new("podcasts", "Podcasts", "播客", "music podcasts"),
        new("energize", "Energize", "活力", "energizing music"),
        new("feel-good", "Feel good", "好心情", "feel good music"),
        new("workout", "Workout", "健身", "workout music"),
        new("relax", "Relax", "放松", "relaxing music"),
        new("party", "Party", "派对", "party music"),
        new("commute", "Commute", "通勤", "commute music"),
        new("focus", "Focus", "专注", "focus music"),
        new("romance", "Romance", "浪漫", "romantic music"),
        new("sad", "Sad", "伤感", "sad songs"),
        new("sleep", "Sleep", "睡眠", "sleep music")
    ];
}

public sealed class HomeDiscoveryService
{
    private readonly IHomeDiscoveryProvider _provider;
    private readonly HomeFeedCache _cache;
    private readonly LibraryService _library;
    private readonly Func<bool> _enabledProvider;
    private CancellationTokenSource? _refreshCts;

    public event Action? Changed;

    public IReadOnlyList<HomeSection> Sections { get; private set; } = Array.Empty<HomeSection>();
    public bool IsRefreshing { get; private set; }
    public DateTimeOffset? LastUpdatedAt { get; private set; }
    public bool IsShowingStale { get; private set; }
    public string? LastRefreshError { get; private set; }
    public HomeFeedScope ActiveScope { get; private set; } = new HomeFeedScope.Guest();
    public bool IsEnabled => _enabledProvider();

    public HomeDiscoveryService(
        IHomeDiscoveryProvider provider,
        HomeFeedCache cache,
        LibraryService library,
        Func<bool>? enabledProvider = null)
    {
        _provider = provider;
        _cache = cache;
        _library = library;
        _enabledProvider = enabledProvider ?? (() => true);
    }

    public void Load()
    {
        _ = LoadAsync(forceReload: false);
    }

    public void Reload()
    {
        _ = LoadAsync(forceReload: true);
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        if (!IsEnabled) return;

        var input = BuildInput();
        ActiveScope = input.Scope;

        var cached = _cache.Get(input, HomeFeedCache.Layer.Baseline);
        var isFresh = cached is not null && _cache.IsFresh(cached, HomeFeedCache.Layer.Baseline);

        if (!forceReload && cached is not null)
        {
            var baselineReason = isFresh ? null : "expired";
            Sections = cached.Value.Sections.Select(s => s.PresentedFromCache(baselineReason)).ToList();
            LastUpdatedAt = cached.Value.FetchedAt;
            IsShowingStale = !isFresh;
            Changed?.Invoke();

            if (isFresh) return;
        }
        else if (Sections.Count == 0)
        {
            Sections = LoadingPlaceholders(input);
            IsShowingStale = false;
            Changed?.Invoke();
        }

        await RefreshAsync(input).ConfigureAwait(false);
    }

    public void Cancel()
    {
        _refreshCts?.Cancel();
        _refreshCts = null;
        IsRefreshing = false;
        Changed?.Invoke();
    }

    private async Task RefreshAsync(HomeDiscoveryInput input)
    {
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;

        IsRefreshing = true;
        Changed?.Invoke();

        try
        {
            var result = await _provider.FetchAsync(input, cts.Token).ConfigureAwait(false);
            if (cts.Token.IsCancellationRequested) return;

            var cachedBaseline = _cache.Get(input, HomeFeedCache.Layer.Baseline);
            var previous = (cachedBaseline?.Value.Sections ?? Sections)
                .ToDictionary(s => s.Id, s => s);

            string? firstFailure = null;
            var merged = result.BaselineSnapshot.Sections.Select(section =>
            {
                if (section.Status is SectionStatus.Failed failed)
                {
                    firstFailure ??= failed.Message;
                    if (previous.TryGetValue(section.Id, out var prev) && prev.Items.Count > 0)
                    {
                        return new HomeSection(
                            prev.Id, prev.Title, prev.Subtitle, prev.Kind,
                            prev.Items, new SectionStatus.Loaded(),
                            HomeSource.Cached, prev.Source,
                            failed.Message ?? prev.StaleReason,
                            prev.FetchedAt);
                    }
                }
                return section;
            }).ToList();

            if (result.StoreBaseline)
            {
                _cache.Set(result.BaselineSnapshot, input, HomeFeedCache.Layer.Baseline);
            }

            Sections = merged;
            LastRefreshError = result.Failures.FirstOrDefault()?.Message ?? firstFailure;
            IsShowingStale = merged.Any(s => s.Source == HomeSource.Cached || s.StaleReason is not null);
            LastUpdatedAt = result.BaselineSnapshot.FetchedAt;
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            LastRefreshError = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke();
        }
    }

    public HomeDiscoveryInput BuildInput()
    {
        var now = DateTime.UtcNow;
        var hour = now.Hour;
        var band = hour switch
        {
            >= 5 and < 12 => ListeningTimeBand.Morning,
            >= 12 and < 18 => ListeningTimeBand.Afternoon,
            >= 18 and < 22 => ListeningTimeBand.Evening,
            _ => ListeningTimeBand.LateNight
        };

        var allTracks = _library.AllTracks();
        var topArtists = allTracks
            .Where(t => t.PlayCount > 0 && !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => t.Artist.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(t => t.PlayCount))
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        var recent = _library.RecentlyPlayedTracks(50)
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .Select(t => t.Artist.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var liked = _library.LikedTracks()
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .Select(t => t.Artist.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var seedVideoIds = _library.RecentlyPlayedTracks(20)
            .Where(t => !string.IsNullOrWhiteSpace(t.YouTubeId))
            .Select(t => t.YouTubeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        return new HomeDiscoveryInput(
            topArtists,
            recent,
            liked,
            band,
            hour,
            seedVideoIds,
            new HomeFeedScope.Guest());
    }

    private static IReadOnlyList<HomeSection> LoadingPlaceholders(HomeDiscoveryInput input)
    {
        var list = new List<HomeSection>();
        if (input.RecentlyPlayedArtistNames.Count > 0)
        {
            list.Add(new HomeSection(
                "listen-again",
                L10n.Tr("Listen again", "再听一次"),
                input.RecentlyPlayedArtistNames[0],
                HomeSectionKind.YouTubeCarousel,
                Array.Empty<DiscoveryItem>(),
                new SectionStatus.Loading()));
        }

        if (input.TopArtistNames.Count > 0)
        {
            list.Add(new HomeSection(
                "top-artist",
                string.Format(L10n.Tr("Mixed for you · {0}", "为你精选 · {0}"), input.TopArtistNames[0]),
                L10n.Tr("From YouTube Music", "来自 YouTube Music"),
                HomeSectionKind.YouTubeCarousel,
                Array.Empty<DiscoveryItem>(),
                new SectionStatus.Loading()));
        }

        list.Add(new HomeSection(
            "quick-picks",
            L10n.Tr("Quick picks", "快速精选"),
            null,
            HomeSectionKind.QuickPicks,
            Array.Empty<DiscoveryItem>(),
            new SectionStatus.Loading()));

        list.Add(SeasonalPlaceholder());

        list.Add(new HomeSection(
            "new-releases",
            L10n.Tr("New releases", "新发行"),
            null,
            HomeSectionKind.YouTubeCarousel,
            Array.Empty<DiscoveryItem>(),
            new SectionStatus.Loading()));

        list.Add(new HomeSection(
            "trending",
            L10n.Tr("Charts", "排行榜"),
            L10n.Tr("From YouTube Music", "来自 YouTube Music"),
            HomeSectionKind.YouTubeCarousel,
            Array.Empty<DiscoveryItem>(),
            new SectionStatus.Loading()));

        return list;
    }

    private static HomeSection SeasonalPlaceholder()
    {
        var month = DateTime.UtcNow.Month;
        var title = month switch
        {
            >= 6 and <= 8 => L10n.Tr("That summer feeling", "夏日氛围"),
            >= 9 and <= 11 => L10n.Tr("Autumn atmosphere", "秋日氛围"),
            12 or 1 or 2 => L10n.Tr("Winter listening", "冬日聆听"),
            _ => L10n.Tr("Spring refresh", "春日焕新")
        };
        return new HomeSection(
            "seasonal",
            title,
            L10n.Tr("Soundtrack the season", "本季原声"),
            HomeSectionKind.YouTubeCarousel,
            Array.Empty<DiscoveryItem>(),
            new SectionStatus.Loading());
    }
}
