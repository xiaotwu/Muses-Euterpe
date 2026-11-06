using Microsoft.Data.Sqlite;
using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Podcasts;
using Muses.Core.Queue;
using Muses.Persistence;

namespace Muses.Tests;

public sealed class PodcastStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    private const string Show = "browse:MPSPshow";
    private const string OtherShow = "browse:MPSPother";
    private const string First = "abcdefghij1";
    private const string Second = "abcdefghij2";
    private const string Third = "abcdefghij3";

    [Fact]
    public void Checkpoints_are_idempotent_and_only_explicit_reset_clears_completion()
    {
        using var store = SqliteStore.Open(inMemory: true);
        store.SaveEpisodes(Show, [new(First, "Long", DurationMs: 300_000), new(Second, "Short", DurationMs: 100_000),
            new(Third, "Unknown length")]);

        Assert.True(store.SaveEpisodeProgress(First, 55_000, null, false, Now));
        Assert.False(store.SaveEpisodeProgress(First, 55_000, null, false, Now.AddSeconds(1)));
        Assert.False(store.SaveEpisodeProgress(First, 90_000, null, false, Now.AddSeconds(-1)));
        Assert.False(store.SaveEpisodeProgress(First, double.NaN, null, false, Now));
        var active = Assert.IsType<PodcastEpisodeState>(store.Episode(First));
        Assert.Equal(55_000, active.LastPositionMs);
        Assert.Equal(Now, active.UpdatedAt);

        Assert.True(store.SaveEpisodeProgress(First, 240_000, null, false, Now.AddSeconds(2)));
        Assert.False(store.SaveEpisodeProgress(First, 0, 300_000, false, Now.AddSeconds(3)));
        Assert.False(store.SaveEpisodeProgress(First, 300_000, null, true, Now.AddSeconds(4)));
        var played = Assert.IsType<PodcastEpisodeState>(store.Episode(First));
        Assert.True(played.Completed);
        Assert.Equal(300_000, played.LastPositionMs);
        Assert.Equal(Now.AddSeconds(2), played.PlayedAt);

        Assert.True(store.SetEpisodePlayed(First, false, Now.AddSeconds(5)));
        Assert.False(store.SetEpisodePlayed(First, false, Now.AddSeconds(6)));
        Assert.False(store.SaveEpisodeProgress(First, 300_000, null, true, Now.AddSeconds(4)));
        var reset = Assert.IsType<PodcastEpisodeState>(store.Episode(First));
        Assert.False(reset.Completed);
        Assert.Equal(0, reset.LastPositionMs);
        Assert.Null(reset.PlayedAt);

        Assert.True(store.SaveEpisodeProgress(Second, 89_000, null, false, Now));
        Assert.False(store.Episode(Second)!.Completed);
        Assert.True(store.SaveEpisodeProgress(Second, 90_000, null, false, Now));
        Assert.True(store.Episode(Second)!.Completed);
        Assert.True(store.SaveEpisodeProgress(Third, 42_000, null, true, Now));
        Assert.True(store.Episode(Third)!.Completed);
        Assert.Null(store.Episode(Third)!.Episode.DurationMs);
        Assert.False(store.SaveEpisodeProgress("abcdefghij4", 1000, null, false, Now));
    }

    [Fact]
    public void Membership_refresh_preserves_progress_and_continue_uses_the_latest_follow()
    {
        using var store = SqliteStore.Open(inMemory: true);
        store.FollowShow(new(Show, "First show", "https://example.com/art.jpg", Now));
        store.FollowShow(new(OtherShow, "Second show", null, Now.AddDays(1)));
        store.FollowShow(new(Show, "Refreshed first show", null, Now.AddDays(2)));
        var followed = store.FollowedShows();
        Assert.Equal([OtherShow, Show], followed.Select(show => show.CatalogId));
        Assert.Equal("Refreshed first show", followed[1].Title);
        Assert.Equal("https://example.com/art.jpg", followed[1].ArtworkUrl);

        store.SaveEpisodes(Show, [new(Second, "Source first"), new(First, "Shared", DurationMs: 300_000)]);
        store.SaveEpisodes(Show, [new(Third, "Next page", Availability: "unavailable")], sourceOffset: 2);
        store.SaveEpisodes(OtherShow, [new(First, "Shared")]);
        Assert.Equal([Second, First, Third], store.Episodes(Show).Select(state => state.Episode.VideoId));
        Assert.True(store.SaveEpisodeProgress(First, 55_000, null, false, Now));
        Assert.True(store.SaveEpisodeProgress(Second, 15_000, null, false, Now.AddSeconds(1)));
        Assert.True(store.SaveEpisodeProgress(Third, 10_000, null, false, Now.AddSeconds(2)));

        store.SaveEpisodes(OtherShow, [new(First, "Shared retitled")]);
        var resumed = store.ContinueEpisodes(12);
        Assert.Equal([Second, First], resumed.Select(item => item.State.Episode.VideoId));
        Assert.Equal(OtherShow, resumed[1].Show.CatalogId);
        Assert.Equal("Shared retitled", resumed[1].State.Episode.Title);
        Assert.Equal(55_000, resumed[1].State.LastPositionMs);
        Assert.Equal(300_000, resumed[1].State.Episode.DurationMs);
        Assert.Single(store.ContinueEpisodes(1));

        store.UnfollowShow(OtherShow);
        Assert.Equal(Show, store.ContinueEpisodes(12)[1].Show.CatalogId);
        store.UnfollowShow(Show);
        Assert.Empty(store.ContinueEpisodes(12));
        Assert.Equal(55_000, store.Episode(First)!.LastPositionMs);
    }

    [Fact]
    public void Adding_podcast_tables_preserves_library_and_queue_on_reopen()
    {
        var directory = Directory.CreateTempSubdirectory("muses-podcast-upgrade-");
        var databasePath = Path.Combine(directory.FullName, "muses-youtube-native.sqlite");
        var libraryTrack = TrackSnapshot.Test("Library track", "existing001") with { Liked = true };
        using var fixture = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        try
        {
            using (var store = SqliteStore.Open(databasePath))
            {
                store.Upsert(TrackEntity.FromSnapshot(libraryTrack));
                store.RecordPlay(libraryTrack.Id);
                var queue = new QueueService { Store = store };
                queue.Play(libraryTrack, [libraryTrack], QueueSource.Songs);
                queue.CheckpointPosition(libraryTrack.Id, 500);
            }

            fixture.Open();
            using (var command = fixture.CreateCommand())
            {
                command.CommandText = """
                    DROP TABLE podcast_episode_memberships;
                    DROP TABLE podcast_episodes;
                    DROP TABLE podcast_shows;
                    """;
                command.ExecuteNonQuery();
            }
            fixture.Close();

            using (var upgraded = SqliteStore.Open(databasePath))
            {
                Assert.False(upgraded.UsedInMemoryFallback, upgraded.FailureDescription);
                upgraded.FollowShow(new(Show, "Show", null, Now));
                upgraded.SaveEpisodes(Show, [new(First, "Episode", DurationMs: 300_000)]);
                Assert.True(upgraded.SaveEpisodeProgress(First, 42_000, null, false, Now));
            }

            using var reopened = SqliteStore.Open(databasePath);
            Assert.False(reopened.UsedInMemoryFallback, reopened.FailureDescription);
            var track = Assert.IsType<TrackEntity>(reopened.Get(libraryTrack.Id));
            Assert.True(track.LibraryMember);
            Assert.True(track.Liked);
            Assert.Equal(1, track.PlayCount);
            var restored = new QueueService { Store = reopened };
            restored.Restore();
            Assert.Equal(libraryTrack.Id, restored.Current()?.Track.Id);
            Assert.Equal(500, restored.LastPositionMs);
            var resume = Assert.Single(reopened.ContinueEpisodes(12));
            Assert.Equal(Show, resume.Show.CatalogId);
            Assert.Equal(First, resume.State.Episode.VideoId);
            Assert.Equal(42_000, resume.State.LastPositionMs);
        }
        finally
        {
            fixture.Close();
            SqliteConnection.ClearPool(fixture);
            directory.Delete(recursive: true);
        }
    }
}
