using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Recommendation;
using Muses.Persistence;

namespace Muses.Tests;

public class SituationalRecommendationTests
{
    [Fact]
    public void SituationalRecommendations_produce_deterministic_sections()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);

        // Add tracks with varying play counts, likes, and dates
        var t1 = new TrackEntity
        {
            Title = "Morning Chill",
            Artist = "Tycho",
            YouTubeId = "vid_tycho",
            PlayCount = 25,
            Liked = true,
            LastPlayedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var t2 = new TrackEntity
        {
            Title = "Evening Beats",
            Artist = "Bonobo",
            YouTubeId = "vid_bonobo",
            PlayCount = 10,
            Liked = false,
            LastPlayedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var t3 = new TrackEntity
        {
            Title = "Old Favorite",
            Artist = "Kiasmos",
            YouTubeId = "vid_kiasmos",
            PlayCount = 50,
            Liked = true,
            LastPlayedAt = DateTimeOffset.UtcNow.AddMonths(-3)
        };

        store.Upsert(t1);
        store.Upsert(t2);
        store.Upsert(t3);

        var service = new SituationalRecommendationService(library);
        var sections = service.GenerateRecommendations();

        Assert.NotEmpty(sections);
        var heavyRotation = sections.FirstOrDefault(s => s.Id == "recently-obsessed" || s.Id == "heavy-rotation");
        Assert.NotNull(heavyRotation);
        Assert.NotEmpty(heavyRotation!.Items);
        // "Old Favorite" and "Morning Chill" have highest plays + liked bonus
        Assert.Contains(heavyRotation.Items, i => i.Title == "Old Favorite");
    }
}
