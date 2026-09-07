using Muses.Core.Domain;
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
