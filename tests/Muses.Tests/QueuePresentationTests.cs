using Muses.Core.Domain;
using Muses.Core.Queue;

namespace Muses.Tests;

public class QueuePresentationTests
{
    private static TrackSnapshot Snap(string title) => TrackSnapshot.Test(title);

    [Fact]
    public void BuildUpcoming_keeps_upNext_and_collection_indices_distinct()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c"), Snap("d") };
        q.Play(ctx[1], ctx, QueueSource.Playlist); // current = b @1
        q.PlayNext(Snap("x"));
        q.AddToQueue(Snap("y"));

        var rows = QueuePresentation.BuildUpcoming(q);
        Assert.Equal(4, rows.Count); // x,y + c,d

        Assert.Equal(QueuePresentationKind.UpNext, rows[0].Kind);
        Assert.Equal(0, rows[0].SourceIndex);
        Assert.Equal("x", rows[0].Item.Track.Title);

        Assert.Equal(QueuePresentationKind.UpNext, rows[1].Kind);
        Assert.Equal(1, rows[1].SourceIndex);
        Assert.Equal("y", rows[1].Item.Track.Title);

        Assert.Equal(QueuePresentationKind.CollectionRemaining, rows[2].Kind);
        Assert.Equal(2, rows[2].SourceIndex); // c in Items
        Assert.Equal("c", rows[2].Item.Track.Title);

        Assert.Equal(QueuePresentationKind.CollectionRemaining, rows[3].Kind);
        Assert.Equal(3, rows[3].SourceIndex);
        Assert.Equal("d", rows[3].Item.Track.Title);
    }

    [Fact]
    public void TryRemove_upNext_does_not_use_collection_RemoveItem_index()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[0], ctx, QueueSource.Playlist);
        q.PlayNext(Snap("x"));
        q.AddToQueue(Snap("y"));

        var rows = QueuePresentation.BuildUpcoming(q);
        // Presentation index 0 is Up Next "x" (source 0). Wrong-list safety:
        // RemoveItem(0) would delete collection "a" (current) — must not happen.
        var upNextRow = rows[0];
        Assert.Equal(QueuePresentationKind.UpNext, upNextRow.Kind);

        Assert.True(QueuePresentation.TryRemove(q, upNextRow));
        Assert.Single(q.UpNext);
        Assert.Equal("y", q.UpNext[0].Track.Title);
        Assert.Equal(3, q.Items.Count); // collection untouched
        Assert.Equal("a", q.Items[0].Track.Title);
    }

    [Fact]
    public void TryRemove_collection_remaining_uses_Items_index()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a"), Snap("b"), Snap("c") };
        q.Play(ctx[0], ctx, QueueSource.Playlist);
        q.PlayNext(Snap("x"));

        var rows = QueuePresentation.BuildUpcoming(q);
        var collectionRow = rows.First(r => r.Kind == QueuePresentationKind.CollectionRemaining && r.Item.Track.Title == "b");
        Assert.Equal(1, collectionRow.SourceIndex);

        Assert.True(QueuePresentation.TryRemove(q, collectionRow));
        Assert.Equal(2, q.Items.Count);
        Assert.DoesNotContain(q.Items, i => i.Track.Title == "b");
        Assert.Single(q.UpNext);
        Assert.Equal("x", q.UpNext[0].Track.Title);
    }

    [Fact]
    public void TryMoveUpNext_reorders_only_upNext()
    {
        var q = new QueueService();
        var ctx = new[] { Snap("a") };
        q.Play(ctx[0], ctx, QueueSource.Playlist);
        q.AddToQueue(Snap("x"));
        q.AddToQueue(Snap("y"));
        q.AddToQueue(Snap("z"));

        Assert.True(QueuePresentation.TryMoveUpNext(q, fromUpNextIndex: 2, toUpNextIndex: 0));
        Assert.Equal(new[] { "z", "x", "y" }, q.UpNext.Select(i => i.Track.Title));
        Assert.Equal("a", q.Items[0].Track.Title);
    }

    [Fact]
    public void Wrong_list_remove_via_presentation_index_zero_is_safe()
    {
        // Regression: drawer used to call RemoveItem(presentationIndex) mixing lists.
        var q = new QueueService();
        var ctx = new[] { Snap("keep-current"), Snap("keep-next") };
        q.Play(ctx[0], ctx, QueueSource.Playlist);
        q.PlayNext(Snap("up-0"));

        var rows = QueuePresentation.BuildUpcoming(q);
        Assert.Equal("up-0", rows[0].Item.Track.Title);

        // Correct API removes Up Next only.
        QueuePresentation.TryRemove(q, rows[0]);
        Assert.Empty(q.UpNext);
        Assert.Equal("keep-current", q.Current()?.Track.Title);
        Assert.Equal(2, q.Items.Count);
    }
}
