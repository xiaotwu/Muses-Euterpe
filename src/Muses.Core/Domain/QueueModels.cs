namespace Muses.Core.Domain;

public sealed class QueueItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required TrackSnapshot Track { get; init; }
    public DateTimeOffset QueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public QueueSource FromContext { get; init; } = QueueSource.Songs;
    public bool Locked { get; set; }
    public Guid? GroupId { get; set; }
    public int? Priority { get; set; }
    public QueueHistoryState? HistoryState { get; set; }
}

public sealed class QueueGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int Order { get; set; }
    public bool Collapsed { get; set; }
}

public sealed class QueueStateRecord
{
    public static readonly Guid SharedId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SharedId;
    public string ItemsJson { get; set; } = "[]";
    public int CurrentIndex { get; set; }
    public string UpNextJson { get; set; } = "[]";
    public string HistoryJson { get; set; } = "[]";
    public string RepeatModeRaw { get; set; } = "off";
    public bool Shuffle { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CurrentTrackId { get; set; }
    public double? LastPositionMs { get; set; }
    public string? GroupsJson { get; set; }
}

public interface IQueueStore
{
    void Save(QueueStateRecord state);
    QueueStateRecord? Load();
}
