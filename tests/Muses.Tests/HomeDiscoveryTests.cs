using Muses.Core.Advanced;
using Muses.Core.Discovery;
using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Preferences;
using Muses.Infrastructure.Advanced;
using Muses.Infrastructure.Discovery;
using Muses.Infrastructure.YTDlp;
using Muses.Persistence;

namespace Muses.Tests;

public class HomeDiscoveryTests
{
    [Fact]
    public void TopPicksResolver_caps_at_max_and_deduplicates()
    {
        var hero = new DiscoveryItem.Track(TrackSnapshot.Test("Hero", "vid-hero"));
        var item1 = new DiscoveryItem.Track(TrackSnapshot.Test("Song 1", "vid-1"));
        var item2 = new DiscoveryItem.Track(TrackSnapshot.Test("Song 2", "vid-2"));
        var duplicateHero = new DiscoveryItem.Track(TrackSnapshot.Test("Hero Duplicate", "vid-hero"));
        var item3 = new DiscoveryItem.Track(TrackSnapshot.Test("Song 3", "vid-3"));
        var item4 = new DiscoveryItem.Track(TrackSnapshot.Test("Song 4", "vid-4"));
        var item5 = new DiscoveryItem.Track(TrackSnapshot.Test("Song 5", "vid-5"));
        var item6 = new DiscoveryItem.Track(TrackSnapshot.Test("Song 6", "vid-6"));

        var picks = TopPicksResolver.Picks(
            hero,
            [item1, item2, duplicateHero, item3],
            [item4, item5, item6],
            max: 6);

        Assert.Equal(6, picks.Count);
        Assert.Equal(hero.Id, picks[0].Id);
        Assert.Equal(item1.Id, picks[1].Id);
        Assert.Equal(item2.Id, picks[2].Id);
        Assert.Equal(item3.Id, picks[3].Id);
        Assert.Equal(item4.Id, picks[4].Id);
        Assert.Equal(item5.Id, picks[5].Id);
    }

    [Fact]
    public void HomeFeedCache_persists_and_reads_stale_and_fresh()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "muses-test-cache-" + Guid.NewGuid());
        try
        {
            var cache = new HomeFeedCache(tempDir, baselineFreshWindow: TimeSpan.FromMinutes(10));
            var input = new HomeDiscoveryInput(
                ["Radiohead"], ["Thom Yorke"], ["Radiohead"],
                ListeningTimeBand.Morning, 9, ["seed1"]);

            var sections = new List<HomeSection>
            {
                new("sec1", "Title 1", "Sub 1", HomeSectionKind.YouTubeCarousel,
                    [new DiscoveryItem.YouTube(new YouTubeDiscoveryCard("v1", "Card 1"))])
            };
            var snapshot = new HomeSnapshot(input.Scope, sections, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30));

            cache.Set(snapshot, input, HomeFeedCache.Layer.Baseline);

            var retrieved = cache.Get(input, HomeFeedCache.Layer.Baseline);
            Assert.NotNull(retrieved);
            Assert.Single(retrieved!.Value.Sections);
            Assert.Equal("Title 1", retrieved.Value.Sections[0].Title);
            Assert.True(cache.IsFresh(retrieved, HomeFeedCache.Layer.Baseline));

            // Future check after fresh window
            var future = DateTimeOffset.UtcNow.AddMinutes(15);
            Assert.False(cache.IsFresh(retrieved, HomeFeedCache.Layer.Baseline, now: future));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Provider_isolates_failures_per_section()
    {
        // Provider where "new-releases" query succeeds, but others throw
        var provider = new YTDlpDiscoveryProvider(
            search: (query, limit, ct) =>
            {
                if (query.Contains("new music"))
                {
                    return Task.FromResult<IReadOnlyList<YTDlpPlaylistEntry>>([
                        new YTDlpPlaylistEntry("v10", "New Song 1", "Artist 1")
                    ]);
                }
                throw new InvalidOperationException("Simulated network timeout for " + query);
            });

        var input = new HomeDiscoveryInput([], [], [], ListeningTimeBand.Afternoon, 14, []);
        var result = await provider.FetchAsync(input);

        Assert.NotEmpty(result.BaselineSnapshot.Sections);
        var loadedSection = result.BaselineSnapshot.Sections.FirstOrDefault(s => s.Id == "new-releases");
        Assert.NotNull(loadedSection);
        Assert.IsType<SectionStatus.Loaded>(loadedSection!.Status);
        Assert.Single(loadedSection.Items);

        var failedSections = result.BaselineSnapshot.Sections.Where(s => s.Id != "new-releases").ToList();
        Assert.NotEmpty(failedSections);
        foreach (var failed in failedSections)
        {
            Assert.IsType<SectionStatus.Failed>(failed.Status);
        }
    }

    [Fact]
    public async Task Superseded_refresh_cannot_publish_or_clear_the_current_refresh()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var tempDir = Path.Combine(Path.GetTempPath(), "muses-home-test-" + Guid.NewGuid());
        try
        {
            var first = new TaskCompletionSource<HomeFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<HomeFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var responses = new Queue<TaskCompletionSource<HomeFetchResult>>([first, second]);
            var provider = new TestHomeProvider((_, _) => responses.Dequeue().Task);
            var cache = new HomeFeedCache(tempDir);
            var service = new HomeDiscoveryService(provider, cache, new LibraryService(store));
            var input = service.BuildInput();

            var oldRefresh = service.LoadAsync(forceReload: true);
            var currentRefresh = service.LoadAsync(forceReload: true);
            first.SetResult(HomeFetchResult.Baseline(input.Scope, [Shelf("old result")]));
            await oldRefresh;

            Assert.True(service.IsRefreshing);
            Assert.DoesNotContain(service.Sections, section => section.Title == "old result");
            Assert.Null(cache.Get(input, HomeFeedCache.Layer.Baseline));

            second.SetResult(HomeFetchResult.Baseline(input.Scope, [Shelf("current result")]));
            await currentRefresh;

            Assert.False(service.IsRefreshing);
            Assert.Equal("current result", Assert.Single(service.Sections).Title);
            Assert.Equal("current result", Assert.Single(cache.Get(input, HomeFeedCache.Layer.Baseline)!.Value.Sections).Title);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private const string AccountChannel = "UCaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherChannel = "UCbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task Account_Home_requires_feed_consent_and_a_matching_channel()
    {
        string? currentChannel = AccountChannel;
        var helper = new FakeWebHomeHelperClient(AccountHomeResponse(AccountChannel));
        var preferences = new MemoryPreferences();
        preferences.SetBool(PrefKey.WebHomeEnabled, true);
        preferences.SetString(PrefKey.WebHomeConsentVersion, "1");
        var session = new WebHomeSessionController(helper, () => currentChannel, preferences);
        var service = new AccountHomeDiscoveryService(session);

        await service.LoadAsync();
        Assert.Equal(0, helper.ExecuteCount);
        Assert.Empty(service.Sections);

        session.EnableWithConsent();
        await service.LoadAsync();
        Assert.Equal("fetchHome", helper.LastRequest!.Action);
        Assert.Equal(AccountChannel, helper.LastRequest.ExpectedChannelId);
        var shelf = Assert.Single(service.Sections);
        Assert.Equal(HomeSource.SignedInWeb, shelf.Source);
        var track = Assert.IsType<DiscoveryItem.YouTube>(shelf.Items[0]);
        Assert.Equal("abcdefghijk", track.Card.PlayableVideoId);
        var album = Assert.IsType<DiscoveryItem.YouTube>(shelf.Items[1]);
        Assert.Null(album.Card.PlayableVideoId);
        Assert.Equal(HomeCardEndpointKind.Browse, album.Card.BrowseEndpoint!.Value.Kind);

        await service.LoadAsync();
        Assert.Equal(1, helper.ExecuteCount);

        currentChannel = OtherChannel;
        Assert.Empty(service.Sections);
        await service.LoadAsync();
        Assert.Equal(WebHomeSessionStatus.AccountMismatch, session.Status);
        Assert.Empty(service.Sections);

        currentChannel = null;
        await session.DisableAndClearAsync();
        await service.LoadAsync();
        Assert.Equal(2, helper.ExecuteCount);
        Assert.Empty(service.Sections);
    }

    [Fact]
    public async Task Cleared_account_Home_ignores_a_late_helper_response()
    {
        var helper = new PendingAccountHomeHelper();
        var session = new WebHomeSessionController(helper, () => AccountChannel, new MemoryPreferences());
        var service = new AccountHomeDiscoveryService(session);
        session.EnableWithConsent();

        var loading = service.LoadAsync();
        await helper.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsRefreshing);

        service.Clear();
        Assert.False(service.IsRefreshing);
        Assert.Empty(service.Sections);
        helper.Response.SetResult(AccountHomeResponse(AccountChannel));
        await loading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(service.Sections);
        Assert.Null(session.VerifiedChannelId);
    }

    private static WebHomeResponse AccountHomeResponse(string channelId)
    {
        var video = new HomeCardEndpoint(HomeCardEndpointKind.Video, "abcdefghijk");
        var album = new HomeCardEndpoint(HomeCardEndpointKind.Browse, "MPREbSyntheticAlbum");
        return WebHomeResponse.Available(channelId) with
        {
            Sections = [new WebHomeSection("account-test", "Fixture recommendations", null, HomeSectionKind.Mixed,
            [
                new WebHomeItem(video, "Fixture song", "Fixture artist", null, null, video, HomeCardAvailability.Available),
                new WebHomeItem(album, "Fixture album", "Fixture artist", null, album, null, HomeCardAvailability.Available)
            ])]
        };
    }

    private sealed class PendingAccountHomeHelper : IWebHomeHelperClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WebHomeResponse> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WebHomeResponse> ExecuteAsync(WebHomeRequest request, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            return Response.Task;
        }

        public Task CancelAsync() => Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_refresh_reuses_saved_shelf_and_keeps_its_timestamp(bool hasFailedSection)
    {
        using var store = SqliteStore.Open(inMemory: true);
        var tempDir = Path.Combine(Path.GetTempPath(), "muses-home-test-" + Guid.NewGuid());
        try
        {
            var provider = new TestHomeProvider((input, _) => Task.FromResult(HomeFetchResult.Baseline(
                input.Scope,
                hasFailedSection ? [new HomeSection("test", "Failed", null, HomeSectionKind.YouTubeCarousel,
                    [], new SectionStatus.Failed("offline"))] : [],
                failures: [new HomeFetchFailure("baseline", "baselineUnavailable", "offline")])));
            var cache = new HomeFeedCache(tempDir);
            var service = new HomeDiscoveryService(provider, cache, new LibraryService(store));
            var input = service.BuildInput();
            var savedAt = DateTimeOffset.UtcNow.AddHours(-1);
            var saved = HomeFetchResult.Baseline(input.Scope, [Shelf("saved result")], fetchedAt: savedAt);
            Assert.True(cache.Set(saved.BaselineSnapshot, input, HomeFeedCache.Layer.Baseline));

            await service.LoadAsync(forceReload: true);

            var shelf = Assert.Single(service.Sections);
            Assert.Equal("saved result", shelf.Title);
            Assert.Equal(HomeSource.Cached, shelf.Source);
            Assert.Equal(HomeSource.PublicDiscovery, shelf.CachedOrigin);
            Assert.Equal("offline", shelf.StaleReason);
            Assert.Equal("offline", service.LastRefreshError);
            Assert.Equal(savedAt, service.LastUpdatedAt);
            Assert.True(service.IsShowingStale);
            Assert.Equal(savedAt, cache.Get(input, HomeFeedCache.Layer.Baseline)!.FetchedAt);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Home_signals_use_local_listening_time_and_case_sensitive_video_ids()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var now = new DateTimeOffset(2026, 11, 5, 4, 0, 0, TimeSpan.Zero);
        store.Upsert(new TrackEntity { YouTubeId = "abcdefghijA", LastPlayedAt = now.AddMinutes(-1) });
        store.Upsert(new TrackEntity { YouTubeId = "abcdefghija", LastPlayedAt = now });
        var provider = new TestHomeProvider((_, _) => throw new InvalidOperationException());
        var service = new HomeDiscoveryService(provider, new HomeFeedCache(), library,
            timeProvider: new TestHomeTimeProvider(now));

        var input = service.BuildInput();

        Assert.Equal(20, input.Hour);
        Assert.Equal(ListeningTimeBand.Evening, input.TimeBand);
        Assert.Equal(new[] { "abcdefghija", "abcdefghijA" }, input.SeedVideoIds);
    }

    private static HomeSection Shelf(string title) => new(
        "test", title, null, HomeSectionKind.YouTubeCarousel,
        [new DiscoveryItem.YouTube(new YouTubeDiscoveryCard("abcdefghijk", title))]);

    private sealed class TestHomeProvider(
        Func<HomeDiscoveryInput, CancellationToken, Task<HomeFetchResult>> fetch) : IHomeDiscoveryProvider
    {
        public bool HasWebEnhancement => false;
        public Task<HomeFetchResult> FetchAsync(HomeDiscoveryInput input, CancellationToken ct = default) => fetch(input, ct);
        public Task<IReadOnlyList<HomeSection>> MoreAsync(int page, HomeDiscoveryInput input, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HomeSection>>([]);
    }

    private sealed class TestHomeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "Test offset", TimeSpan.FromHours(-8), "Test offset", "Test offset");
    }

    [Fact]
    public void Mood_chips_have_eleven_predefined_moods()
    {
        Assert.Equal(11, YouTubeHomeMood.All.Count);
        Assert.Contains(YouTubeHomeMood.All, m => m.Id == "focus" && m.SearchQuery == "focus music");
        Assert.Contains(YouTubeHomeMood.All, m => m.Id == "energize" && m.SearchQuery == "energizing music");
    }
}
