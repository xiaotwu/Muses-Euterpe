using Muses.Core.Domain;
using Muses.Core.Queue;

namespace Muses.Tests;

public class QueueServiceTests
{
    private static TrackSnapshot Snap(string title) => TrackSnapshot.Test(title);

    [Fact]
    public void Play_sets_context_and_positions_index()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[1], ctx, QueueSource.Album);
        Assert.Equal(3, q.Items.Count);
        Assert.Equal(1, q.CurrentIndex);
        Assert.Equal("b", q.Current()?.Track.Title);
    }

    [Fact]
    public void PlayNext_inserts_to_upNext_head()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a") };
        q.Play(ctx[0], ctx, QueueSource.Album);
        q.PlayNext(Snap("x"));
        q.PlayNext(Snap("y"));
        Assert.Equal(2, q.UpNext.Count);
        Assert.Equal("y", q.UpNext[0].Track.Title);
    }

    [Fact]
    public void ClearUpNext_only_clears_manual_insertions()
    {
        var q = new QueueService();
        var collection = new[] { Snap("a"), Snap("b") };
        q.Play(collection[0], collection, QueueSource.Playlist);
        q.PlayNext(Snap("manual"));

        q.ClearUpNext();

        Assert.Empty(q.UpNext);
        Assert.Equal(collection[1].Id, q.Next()?.Track.Id);
    }

    [Fact]
    public void Next_drains_upNext_then_context_and_writes_history()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b") };
        q.Play(ctx[0], ctx, QueueSource.Album);
        q.PlayNext(Snap("x"));
        var n1 = q.Next();
        Assert.Equal("x", n1?.Track.Title);
        Assert.Equal("a", q.History[0].Track.Title);
        Assert.Empty(q.UpNext);
        var n2 = q.Next();
        Assert.Equal("b", n2?.Track.Title);
    }

    [Fact]
    public void Repeat_one_keeps_current()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b") };
        q.Play(ctx[0], ctx, QueueSource.Album);
        q.SetRepeat(RepeatMode.One);
        Assert.Equal("a", q.Next()?.Track.Title);
    }

    [Fact]
    public void Repeat_all_wraps()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b") };
        q.Play(ctx[1], ctx, QueueSource.Album);
        q.SetRepeat(RepeatMode.All);
        _ = q.Next();
        Assert.Equal("a", q.Next()?.Track.Title);
    }

    [Fact]
    public void Previous_navigates_history()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b") };
        q.Play(ctx[0], ctx, QueueSource.Album);
        _ = q.Next();
        var p = q.Previous();
        Assert.Equal("a", p?.Track.Title);
        Assert.Equal(0, q.CurrentIndex);
        Assert.Equal("a", q.Current()?.Track.Title);
    }

    [Fact]
    public void Turning_shuffle_off_keeps_the_playing_track_as_current()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[1], ctx, QueueSource.Album);
        q.ToggleShuffle();
        Assert.Equal("b", q.Current()?.Track.Title);
        q.ToggleShuffle();
        Assert.Equal("b", q.Current()?.Track.Title);
        Assert.Equal(1, q.CurrentIndex);
        Assert.Equal(new[] { "a", "b", "c" }, q.Items.Select(i => i.Track.Title));
    }

    [Fact]
    public void Restore_keeps_manual_occurrence_and_unshuffle_keeps_only_existing_items()
    {
        var store = new MemoryQueueStore();
        var queue = new QueueService { Store = store };
        var context = new[] { Snap("a"), Snap("b"), Snap("c") };
        queue.Play(context[0], context, QueueSource.Playlist);
        var originalIds = queue.Items.Select(item => item.Id).ToArray();
        queue.ToggleShuffle();
        queue.RemoveItem(queue.Items.FindIndex(item => item.Id == originalIds[1]));
        queue.PlayNext(context[0]);
        var manual = queue.Next()!;
        queue.CheckpointPosition(manual.Track.Id, 1200);

        var restored = new QueueService { Store = store };
        restored.Restore();
        Assert.Equal(manual.Id, restored.Current()?.Id);
        Assert.Equal(1200, restored.LastPositionMs);
        Assert.Equal(originalIds[0], restored.Items[restored.CurrentIndex].Id);
        Assert.True(restored.Shuffle);

        restored.ToggleShuffle();
        Assert.Equal(new[] { originalIds[0], originalIds[2] }, restored.Items.Select(item => item.Id));
        Assert.Equal(manual.Id, restored.Current()?.Id);
        Assert.Equal(originalIds[2], restored.Next()?.Id);
    }

    [Fact]
    public void PeekNext_follows_collection_then_wraps_on_repeat_all()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[1], ctx, QueueSource.Album);
        Assert.Equal("c", q.PeekNext()?.Track.Title);
        q.Play(ctx[2], ctx, QueueSource.Album);
        Assert.Null(q.PeekNext());
        q.SetRepeat(RepeatMode.All);
        Assert.Equal("a", q.PeekNext()?.Track.Title);
        q.PlayNext(Snap("x"));
        Assert.Equal("x", q.PeekNext()?.Track.Title);
    }

    [Fact]
    public void Recently_played_context_keeps_full_list_and_tapped_index()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[1], ctx, QueueSource.Recently);
        Assert.Equal(3, q.Items.Count);
        Assert.Equal(1, q.CurrentIndex);
        Assert.Equal("c", q.Next()?.Track.Title);
    }

    [Fact]
    public void Playlist_context_of_three_snaps_positions_last_tap_at_index_2()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[2], ctx, QueueSource.Playlist);
        Assert.Equal(3, q.Items.Count);
        Assert.Equal(2, q.CurrentIndex);
        Assert.Equal("c", q.Current()?.Track.Title);
    }

    private sealed class MemoryQueueStore : IQueueStore
    {
        private QueueStateRecord? _state;
        public void Save(QueueStateRecord state) => _state = state;
        public QueueStateRecord? Load() => _state;
    }
}
