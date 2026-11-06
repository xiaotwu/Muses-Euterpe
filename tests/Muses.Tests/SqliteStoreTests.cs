using Microsoft.Data.Sqlite;
using Muses.Core.Advanced;
using Muses.Core.Domain;
using Muses.Core.History;
using Muses.Core.Library;
using Muses.Core.Queue;
using Muses.Persistence;

namespace Muses.Tests;

public class SqliteStoreTests
{
    [Fact]
    public void Persist_then_restore_preserves_queue_state()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var q = new QueueService { Store = store };
        var ctx = new[] { TrackSnapshot.Test("a"), TrackSnapshot.Test("b"), TrackSnapshot.Test("c") };
        q.Play(ctx[1], ctx, QueueSource.Album);
        q.Persist();

        var q2 = new QueueService { Store = store };
        q2.Restore();
        Assert.Equal(3, q2.Items.Count);
        Assert.Equal(1, q2.CurrentIndex);
        Assert.Equal("b", q2.Current()?.Track.Title);
    }

    [Fact]
    public void Move_preserves_order_through_persist_restore()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var q = new QueueService { Store = store };
        var ctx = new[] { TrackSnapshot.Test("a"), TrackSnapshot.Test("b"), TrackSnapshot.Test("c") };
        q.Play(ctx[0], ctx, QueueSource.Album);
        q.Move(0, 2);
        Assert.Equal(new[] { "b", "c", "a" }, q.Items.Select(i => i.Track.Title));
        Assert.Equal(2, q.CurrentIndex);

        var q2 = new QueueService { Store = store };
        q2.Restore();
        Assert.Equal(new[] { "b", "c", "a" }, q2.Items.Select(i => i.Track.Title));
        Assert.Equal(2, q2.CurrentIndex);
    }

    [Fact]
    public void Legacy_database_upgrade_preserves_library_history_notes_and_queue()
    {
        var directory = Directory.CreateTempSubdirectory("muses-sqlite-upgrade-");
        var databasePath = Path.Combine(directory.FullName, "muses-youtube-native.sqlite");
        using var fixture = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        var first = TrackSnapshot.Test("First", "video-a", 180) with { Liked = true };
        var second = TrackSnapshot.Test("Second", "video-b", 210);
        var manual = TrackSnapshot.Test("Manual", "video-c", 200);
        var firstId = first.Id.ToString();
        var history = new ListeningEventEntity("legacy-event", firstId, 1_700_000_000_000,
            1_700_000_080_000, ListeningOutcome.Skipped, null, 80_000);
        var bookmark = new TrackBookmarkEntity("legacy-bookmark", firstId, 12_500, "Intro", "Cue", 1_700_000_050_000);
        var group = new QueueGroup { Name = "Later", Order = 0, Collapsed = true };

        try
        {
            Guid playlistId;
            Guid[] playlistItemIds;
            TrackNoteEntity? note;
            using (var oldStore = SqliteStore.Open(databasePath))
            {
                oldStore.Upsert(TrackEntity.FromSnapshot(first));
                oldStore.Upsert(TrackEntity.FromSnapshot(second));
                oldStore.RecordPlay(first.Id);
                oldStore.RecordEvent(history);
                oldStore.SetNote(firstId, "Saved note");
                oldStore.AddBookmark(bookmark);
                note = oldStore.GetNote(firstId);
                Assert.NotNull(note);

                playlistId = oldStore.Create("Legacy playlist").Id;
                oldStore.AddTrack(playlistId, first.Id);
                oldStore.AddTrack(playlistId, second.Id);
                oldStore.MoveItem(playlistId, 1, 0);
                oldStore.TogglePin(playlistId);
                playlistItemIds = oldStore.GetPlaylist(playlistId)!.Items.Select(item => item.Id).ToArray();

                var queue = new QueueService { Store = oldStore };
                queue.Play(first, [first, second], QueueSource.Playlist);
                queue.Next();
                queue.PlayNext(manual);
                queue.Groups.Add(group);
                queue.UpNext[0].GroupId = group.Id;
                queue.SetRepeat(RepeatMode.All);
                queue.CheckpointPosition(second.Id, 54_321.5);
            }

            // Reconstruct the on-disk schema from before the additive columns.
            fixture.Open();
            using (var command = fixture.CreateCommand())
            {
                command.CommandText = """
                    ALTER TABLE queue_state DROP COLUMN inserted_current_json;
                    ALTER TABLE queue_state DROP COLUMN original_order_json;
                    ALTER TABLE tracks DROP COLUMN library_member;
                    """;
                command.ExecuteNonQuery();
            }
            fixture.Close();

            Guid manualOccurrenceId;
            using (var store = SqliteStore.Open(databasePath))
            {
                Assert.False(store.UsedInMemoryFallback, store.FailureDescription);
                var track = Assert.IsType<TrackEntity>(store.GetByYouTubeId(first.YouTubeId));
                Assert.Equal(first.Id, track.Id);
                Assert.True(track.LibraryMember);
                Assert.True(track.Liked);
                Assert.Equal(1, track.PlayCount);
                Assert.NotNull(track.LastPlayedAt);
                Assert.Equal(history, Assert.Single(store.GetEvents()));
                Assert.Equal(note, store.GetNote(firstId));
                Assert.Equal(bookmark, Assert.Single(store.GetBookmarks(firstId)));

                var playlist = Assert.IsType<PlaylistEntity>(store.GetPlaylist(playlistId));
                Assert.True(playlist.Pinned);
                Assert.Equal(playlistItemIds, playlist.Items.Select(item => item.Id));
                Assert.Equal(new Guid?[] { second.Id, first.Id }, playlist.Items.Select(item => item.TrackId));

                var queue = new QueueService { Store = store };
                queue.Restore();
                Assert.Equal(1, queue.CurrentIndex);
                Assert.Equal(second.Id, queue.Current()?.Track.Id);
                Assert.Equal(second.Id, queue.CurrentTrackId);
                Assert.Equal(54_321.5, queue.LastPositionMs);
                Assert.Equal(RepeatMode.All, queue.RepeatMode);
                Assert.Equal(first.Id, Assert.Single(queue.History).Track.Id);
                Assert.Equal(group.Id, Assert.Single(queue.Groups).Id);
                var upNext = Assert.Single(queue.UpNext);
                Assert.Equal(group.Id, upNext.GroupId);
                Assert.Equal(manual.Id, upNext.Track.Id);
                manualOccurrenceId = upNext.Id;

                queue.Next();
                queue.CheckpointPosition(manual.Id, 900);
            }

            using var reopened = SqliteStore.Open(databasePath);
            Assert.False(reopened.UsedInMemoryFallback, reopened.FailureDescription);
            var restored = new QueueService { Store = reopened };
            restored.Restore();
            Assert.Equal(manualOccurrenceId, restored.Current()?.Id);
            Assert.Equal(manual.Id, restored.CurrentTrackId);
            Assert.Equal(900, restored.LastPositionMs);
            Assert.NotNull(reopened.Load()?.OriginalOrderJson);
        }
        finally
        {
            fixture.Close();
            SqliteConnection.ClearPool(fixture);
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RecordPlay_updates_persisted_youtube_track()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var snap = library.UpsertFromYouTube("video-a", "Track", "Artist", 1, null);
        library.RecordPlay(snap.Id);
        library.RecordPlay(snap.Id);
        var saved = store.Get(snap.Id);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.PlayCount);
        Assert.NotNull(saved.LastPlayedAt);
        Assert.Equal(2, library.PlayRevision);
    }

    [Fact]
    public void Recently_played_deduplicates_video_ids()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var older = TrackEntity.FromSnapshot(TrackSnapshot.Test("Older", "same"));
        older.LastPlayedAt = DateTimeOffset.UtcNow.AddSeconds(-120);
        var newer = TrackEntity.FromSnapshot(TrackSnapshot.Test("Newer", "same"));
        newer.LastPlayedAt = DateTimeOffset.UtcNow;
        var distinct = TrackEntity.FromSnapshot(TrackSnapshot.Test("Distinct", "other"));
        distinct.LastPlayedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        store.Upsert(older);
        store.Upsert(newer);
        store.Upsert(distinct);
        var recent = new LibraryService(store).RecentlyPlayedTracks(10);
        Assert.Equal(new[] { "Newer", "Distinct" }, recent.Select(t => t.Title));
    }
}
