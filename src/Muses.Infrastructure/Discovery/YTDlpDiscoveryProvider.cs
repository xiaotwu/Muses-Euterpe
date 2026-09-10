using Muses.Core.Discovery;
using Muses.Core.L10n;
using Muses.Infrastructure.YTDlp;

namespace Muses.Infrastructure.Discovery;

public sealed class YTDlpDiscoveryProvider : IHomeDiscoveryProvider
{
    private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<YTDlpPlaylistEntry>>> _search;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<YTDlpPlaylistEntry>>>? _fetchPlaylist;
    private readonly int _perSectionLimit;
    private readonly int _displayLimit;

    public bool HasWebEnhancement => false;

    public YTDlpDiscoveryProvider(
        IYTDlpBridge bridge,
        int perSectionLimit = 12,
        int displayLimit = 10)
        : this(
            (q, lim, ct) => bridge.SearchAsync(q, lim, TimeSpan.FromSeconds(25), ct),
            (url, ct) => bridge.FetchPlaylistAsync(url, TimeSpan.FromSeconds(35), ct),
            perSectionLimit,
            displayLimit)
    {
    }

    public YTDlpDiscoveryProvider(
        Func<string, int, CancellationToken, Task<IReadOnlyList<YTDlpPlaylistEntry>>> search,
        Func<string, CancellationToken, Task<IReadOnlyList<YTDlpPlaylistEntry>>>? fetchPlaylist = null,
        int perSectionLimit = 12,
        int displayLimit = 10)
    {
        _search = search;
        _fetchPlaylist = fetchPlaylist;
        _perSectionLimit = perSectionLimit;
        _displayLimit = displayLimit;
    }

    public async Task<HomeFetchResult> FetchAsync(HomeDiscoveryInput input, CancellationToken ct = default)
    {
        var plans = SectionPlans(input);
        var tasks = plans.Select(plan => RunSectionAsync(plan, input, ct)).ToList();
        var sections = await Task.WhenAll(tasks).ConfigureAwait(false);

        var failures = sections
            .Where(s => s.Status is SectionStatus.Failed)
            .Select(s => new HomeFetchFailure("baseline", "baselineUnavailable", ((SectionStatus.Failed)s.Status).Message))
            .ToList();

        return HomeFetchResult.Baseline(input.Scope, sections, DateTimeOffset.UtcNow, failures);
    }

    public async Task<IReadOnlyList<HomeSection>> MoreAsync(int page, HomeDiscoveryInput input, CancellationToken ct = default)
    {
        var seeds = input.TopArtistNames.Concat(input.LikedArtistNames).ToList();
        var seed = seeds.Count == 0 ? "music" : seeds[Math.Abs(page) % seeds.Count];
        var year = DateTime.UtcNow.Year;
        var queries = new[]
        {
            $"{seed} mix",
            $"new music {year}",
            $"recommended {seed}",
            $"playlist {seed} radio"
        };
        var query = queries[Math.Abs(page) % queries.Length];
        var plan = new SectionPlan(
            $"more-{page}",
            L10n.Tr("More for you", "更多推荐"),
            L10n.Tr("From YouTube Music", "来自 YouTube Music"),
            query);

        var section = await RunSectionAsync(plan, input, ct).ConfigureAwait(false);
        return [section];
    }

    private sealed record SectionPlan(string Id, string Title, string? Subtitle, string Query, string? Url = null);

    private IReadOnlyList<SectionPlan> SectionPlans(HomeDiscoveryInput input)
    {
        var plans = new List<SectionPlan>();
        var year = DateTime.UtcNow.Year;

        plans.Add(new SectionPlan(
            "new-releases",
            L10n.Tr("New Releases", "新发行"),
            L10n.Tr("From YouTube Music", "来自 YouTube Music"),
            $"new music {year} official audio"));

        var quickQuery = input.TopArtistNames.Count > 0
            ? $"{input.TopArtistNames[0]} popular songs mix"
            : "popular music mix";
        plans.Add(new SectionPlan(
            "quick-picks",
            L10n.Tr("Quick picks", "快速精选"),
            L10n.Tr("Play all", "全部播放"),
            quickQuery));

        plans.Add(SeasonalPlan());

        if (input.RecentlyPlayedArtistNames.Count > 0)
        {
            var recent = input.RecentlyPlayedArtistNames[0];
            plans.Add(new SectionPlan(
                "listen-again",
                L10n.Tr("Listen again", "再听一次"),
                L10n.Tr("From YouTube Music", "来自 YouTube Music"),
                $"{recent} mix"));
        }
        else if (input.SeedVideoIds.Count > 0)
        {
            plans.Add(new SectionPlan(
                "listen-again",
                L10n.Tr("Listen again", "再听一次"),
                L10n.Tr("From YouTube Music", "来自 YouTube Music"),
                "music mix"));
        }

        if (input.TopArtistNames.Count > 0)
        {
            var artist = input.TopArtistNames[0];
            plans.Add(new SectionPlan(
                "top-artist",
                string.Format(L10n.Tr("Mixed for you · {0}", "为你精选 · {0}"), artist),
                L10n.Tr("From YouTube Music", "来自 YouTube Music"),
                $"{artist} mix radio"));
        }

        if (input.LikedArtistNames.Count > 0 &&
            (input.TopArtistNames.Count == 0 || !string.Equals(input.LikedArtistNames[0], input.TopArtistNames[0], StringComparison.OrdinalIgnoreCase)))
        {
            var liked = input.LikedArtistNames[0];
            plans.Add(new SectionPlan(
                "from-liked",
                string.Format(L10n.Tr("Because you like {0}", "因为你喜欢 {0}"), liked),
                L10n.Tr("From YouTube Music", "来自 YouTube Music"),
                $"{liked} essentials playlist"));
        }

        plans.Add(new SectionPlan(
            "trending",
            L10n.Tr("Charts", "排行榜"),
            L10n.Tr("From YouTube Music", "来自 YouTube Music"),
            $"trending charts {year}"));

        return plans;
    }

    private static SectionPlan SeasonalPlan()
    {
        var month = DateTime.UtcNow.Month;
        return month switch
        {
            >= 6 and <= 8 => new SectionPlan("seasonal", L10n.Tr("That summer feeling", "夏日氛围"), L10n.Tr("Soundtrack the season", "本季原声"), "summer music playlist"),
            >= 9 and <= 11 => new SectionPlan("seasonal", L10n.Tr("Autumn atmosphere", "秋日氛围"), L10n.Tr("Soundtrack the season", "本季原声"), "autumn music playlist"),
            12 or 1 or 2 => new SectionPlan("seasonal", L10n.Tr("Winter listening", "冬日聆听"), L10n.Tr("Soundtrack the season", "本季原声"), "winter music playlist"),
            _ => new SectionPlan("seasonal", L10n.Tr("Spring refresh", "春日焕新"), L10n.Tr("Soundtrack the season", "本季原声"), "spring music playlist")
        };
    }

    private async Task<HomeSection> RunSectionAsync(SectionPlan plan, HomeDiscoveryInput input, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<YTDlpPlaylistEntry> entries;
            if (plan.Url is not null && _fetchPlaylist is not null)
            {
                try
                {
                    entries = await _fetchPlaylist(plan.Url, ct).ConfigureAwait(false);
                    if (entries.Count < Math.Min(4, _displayLimit))
                    {
                        entries = await _search(plan.Query, _perSectionLimit, ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                    entries = await _search(plan.Query, _perSectionLimit, ct).ConfigureAwait(false);
                }
            }
            else
            {
                entries = await _search(plan.Query, _perSectionLimit, ct).ConfigureAwait(false);
            }

            var cards = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Title))
                .Select(e => new YouTubeDiscoveryCard(
                    e.Id,
                    e.Title,
                    e.Uploader ?? "YouTube Music",
                    e.Duration,
                    $"https://i.ytimg.com/vi/{e.Id}/hqdefault.jpg"))
                .ToList();

            var ranked = LightlyRank(cards, input).Take(_displayLimit).ToList();
            var items = ranked.Select(c => (DiscoveryItem)new DiscoveryItem.YouTube(c)).ToList();

            var kind = plan.Id == "quick-picks" ? HomeSectionKind.QuickPicks : HomeSectionKind.YouTubeCarousel;
            return new HomeSection(
                plan.Id,
                plan.Title,
                plan.Subtitle,
                kind,
                items,
                new SectionStatus.Loaded());
        }
        catch (OperationCanceledException)
        {
            return new HomeSection(
                plan.Id, plan.Title, plan.Subtitle, HomeSectionKind.YouTubeCarousel,
                Array.Empty<DiscoveryItem>(), new SectionStatus.Failed(null));
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Length > 180) msg = msg[..180] + "…";
            return new HomeSection(
                plan.Id, plan.Title, plan.Subtitle, HomeSectionKind.YouTubeCarousel,
                Array.Empty<DiscoveryItem>(), new SectionStatus.Failed(msg));
        }
    }

    private static IReadOnlyList<YouTubeDiscoveryCard> LightlyRank(
        IReadOnlyList<YouTubeDiscoveryCard> cards,
        HomeDiscoveryInput input)
    {
        var signals = input.TopArtistNames
            .Concat(input.RecentlyPlayedArtistNames)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .ToHashSet();

        if (signals.Count == 0) return cards;

        return cards
            .Select((card, index) => (card, index, score: (card.Uploader is not null && signals.Contains(card.Uploader.Trim().ToLowerInvariant())) ? 1 : 0))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Select(x => x.card)
            .ToList();
    }
}
