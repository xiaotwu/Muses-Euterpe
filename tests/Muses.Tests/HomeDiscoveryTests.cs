using Muses.Core.Discovery;
using Muses.Core.Domain;
using Muses.Core.Library;
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
    public void Mood_chips_have_eleven_predefined_moods()
    {
        Assert.Equal(11, YouTubeHomeMood.All.Count);
        Assert.Contains(YouTubeHomeMood.All, m => m.Id == "focus" && m.SearchQuery == "focus music");
        Assert.Contains(YouTubeHomeMood.All, m => m.Id == "energize" && m.SearchQuery == "energizing music");
    }
}
