using Muses.Core.Domain;

namespace Muses.Core.Queue;

/// <summary>
/// Which backing list a drawer row indexes into. Mixing Up Next with collection
/// remaining under one RemoveItem index is unsafe — always use this mapping.
/// </summary>
public enum QueuePresentationKind
{
    UpNext,
    CollectionRemaining,
    History
}

public sealed record QueuePresentationRow(
    QueuePresentationKind Kind,
    int SourceIndex,
    QueueItem Item
);

/// <summary>
/// Builds drawer presentation rows with correct source indices for remove/reorder.
/// </summary>
public static class QueuePresentation
{
    public static IReadOnlyList<QueuePresentationRow> BuildUpcoming(QueueService queue)
    {
        var rows = new List<QueuePresentationRow>();
        for (var i = 0; i < queue.UpNext.Count; i++)
            rows.Add(new QueuePresentationRow(QueuePresentationKind.UpNext, i, queue.UpNext[i]));

        if (queue.CurrentIndex >= 0)
        {
            for (var i = queue.CurrentIndex + 1; i < queue.Items.Count; i++)
                rows.Add(new QueuePresentationRow(QueuePresentationKind.CollectionRemaining, i, queue.Items[i]));
        }

        return rows;
    }

    public static IReadOnlyList<QueuePresentationRow> BuildHistory(QueueService queue, int take = 10)
    {
        var hist = queue.History;
        var start = Math.Max(0, hist.Count - take);
        var rows = new List<QueuePresentationRow>();
        // Newest first for display; SourceIndex stays the real History list index.
        for (var i = hist.Count - 1; i >= start; i--)
            rows.Add(new QueuePresentationRow(QueuePresentationKind.History, i, hist[i]));
        return rows;
    }

    public static bool TryRemove(QueueService queue, QueuePresentationRow row)
    {
        switch (row.Kind)
        {
            case QueuePresentationKind.UpNext:
                return queue.RemoveUpNext(row.SourceIndex);
            case QueuePresentationKind.CollectionRemaining:
                if (row.SourceIndex == queue.CurrentIndex) return false;
                queue.RemoveItem(row.SourceIndex);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reorder within Up Next only. Collection remaining rows are not drag-reordered here.
    /// </summary>
    public static bool TryMoveUpNext(QueueService queue, int fromUpNextIndex, int toUpNextIndex)
    {
        if (fromUpNextIndex < 0 || fromUpNextIndex >= queue.UpNext.Count) return false;
        if (toUpNextIndex < 0 || toUpNextIndex >= queue.UpNext.Count) return false;
        if (fromUpNextIndex == toUpNextIndex) return false;
        queue.MoveUpNext(fromUpNextIndex, toUpNextIndex);
        return true;
    }
}
