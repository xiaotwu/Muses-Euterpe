using System.Text.Json;
using System.Text.Json.Serialization;
using Muses.Core.Domain;

namespace Muses.Core.Queue;

public sealed class QueueService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public IQueueStore? Store { get; set; }

    public List<QueueItem> Items { get; } = [];
    public int CurrentIndex { get; set; } = -1;
    public List<QueueItem> UpNext { get; } = [];
    public List<QueueItem> History { get; } = [];
    public RepeatMode RepeatMode { get; private set; } = RepeatMode.Off;
    public bool Shuffle { get; private set; }
    public Guid? CurrentTrackId { get; set; }
    public double? LastPositionMs { get; set; }
    public List<QueueGroup> Groups { get; } = [];
    public bool ReplacementLocked { get; set; }

    private List<QueueItem> _originalOrder = [];

    public QueueItem? Current()
    {
        if (CurrentIndex < 0 || CurrentIndex >= Items.Count) return null;
        return Items[CurrentIndex];
    }

    public void Play(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context, QueueSource from)
    {
        if (ReplacementLocked)
        {
            var existing = Items.FindIndex(i => i.Track.Id == track.Id);
            if (existing >= 0)
            {
                CurrentIndex = existing;
                Persist();
                return;
            }
            PlayNext(track);
            return;
        }

        Items.Clear();
        foreach (var t in context)
        {
            Items.Add(new QueueItem { Track = t, FromContext = from });
        }
        _originalOrder = [.. Items];
        CurrentIndex = context.ToList().FindIndex(t => t.Id == track.Id);
        if (CurrentIndex < 0) CurrentIndex = 0;
        UpNext.Clear();
        Persist();
    }

    public void PlayNext(TrackSnapshot track)
    {
        UpNext.Insert(0, new QueueItem { Track = track });
        Persist();
    }

    public void AddToQueue(TrackSnapshot track)
    {
        UpNext.Add(new QueueItem { Track = track });
        Persist();
    }

    public QueueItem? Next(QueueHistoryState state = QueueHistoryState.Played)
    {
        if (Current() is { } cur)
        {
            cur.HistoryState = state;
            History.Insert(0, cur);
            if (History.Count > 200) History.RemoveAt(History.Count - 1);
        }

        SortUpNextByPriority();
        if (UpNext.Count > 0)
        {
            var popped = UpNext[0];
            UpNext.RemoveAt(0);
            Persist();
            return popped;
        }

        if (Items.Count == 0) return null;
        switch (RepeatMode)
        {
            case RepeatMode.One:
                return Current();
            case RepeatMode.All:
                if (CurrentIndex >= Items.Count)
                {
                    CurrentIndex = 0;
                    Persist();
                    return Current();
                }
                var allNext = CurrentIndex + 1;
                if (allNext < Items.Count)
                {
                    CurrentIndex = allNext;
                    Persist();
                    return Current();
                }
                CurrentIndex = allNext;
                Persist();
                return null;
            default:
                var next = CurrentIndex + 1;
                if (next >= Items.Count) return null;
                CurrentIndex = next;
                Persist();
                return Current();
        }
    }

    public QueueItem? Previous()
    {
        if (History.Count > 0)
        {
            var h = History[0];
            History.RemoveAt(0);
            var idx = Items.FindIndex(i => i.Id == h.Id);
            if (idx >= 0) CurrentIndex = idx;
            Persist();
            return h;
        }
        if (CurrentIndex <= 0) return Current();
        CurrentIndex--;
        Persist();
        return Current();
    }

    public void SetRepeat(RepeatMode mode)
    {
        RepeatMode = mode;
        Persist();
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        if (Shuffle)
        {
            _originalOrder = [.. Items];
            var cur = Current();
            ShuffleUnlockedItems();
            if (cur is not null)
            {
                var idx = Items.FindIndex(i => i.Id == cur.Id);
                if (idx >= 0) CurrentIndex = idx;
            }
        }
        else
        {
            var cur = Current();
            Items.Clear();
            Items.AddRange(_originalOrder);
            if (cur is not null)
            {
                var idx = Items.FindIndex(i => i.Id == cur.Id);
                CurrentIndex = idx >= 0 ? idx : Math.Clamp(CurrentIndex, 0, Math.Max(Items.Count - 1, 0));
            }
        }
        Persist();
    }

    public QueueItem? PeekNext()
    {
        SortUpNextByPriority();
        if (UpNext.Count > 0) return UpNext[0];
        if (Items.Count == 0) return null;
        var following = CurrentIndex + 1;
        if (following < Items.Count) return Items[following];
        if (RepeatMode == RepeatMode.All) return Items[0];
        return null;
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Items.Count) return;
        var curId = Current()?.Id;
        MoveList(Items, from, to);
        if (curId is { } id)
        {
            var idx = Items.FindIndex(i => i.Id == id);
            if (idx >= 0) CurrentIndex = idx;
        }
        Persist();
    }

    public void MoveUpNext(int from, int to)
    {
        if (from < 0 || from >= UpNext.Count) return;
        MoveList(UpNext, from, to);
        Persist();
    }

    public void Persist()
    {
        if (Store is null) return;
        Store.Save(new QueueStateRecord
        {
            ItemsJson = JsonSerializer.Serialize(Items, Json),
            CurrentIndex = CurrentIndex,
            UpNextJson = JsonSerializer.Serialize(UpNext, Json),
            HistoryJson = JsonSerializer.Serialize(History, Json),
            RepeatModeRaw = RepeatModeCodec.Wire(RepeatMode),
            Shuffle = Shuffle,
            CurrentTrackId = CurrentTrackId,
            LastPositionMs = LastPositionMs,
            GroupsJson = JsonSerializer.Serialize(Groups, Json),
            SavedAt = DateTimeOffset.UtcNow
        });
    }

    public void Restore()
    {
        if (Store?.Load() is not { } row) return;
        Replace(Items, Deserialize<List<QueueItem>>(row.ItemsJson) ?? []);
        Replace(UpNext, Deserialize<List<QueueItem>>(row.UpNextJson) ?? []);
        Replace(History, Deserialize<List<QueueItem>>(row.HistoryJson) ?? []);
        var groups = Deserialize<List<QueueGroup>>(row.GroupsJson ?? "[]") ?? [];
        Replace(Groups, groups.OrderBy(g => g.Order).ToList());
        CurrentIndex = row.CurrentIndex;
        RepeatMode = RepeatModeCodec.Parse(row.RepeatModeRaw);
        Shuffle = row.Shuffle;
        CurrentTrackId = row.CurrentTrackId;
        LastPositionMs = row.LastPositionMs;
        _originalOrder = [.. Items];
    }

    public void CheckpointPosition(Guid? currentTrackId, double? lastPositionMs)
    {
        CurrentTrackId = currentTrackId;
        LastPositionMs = lastPositionMs;
        Persist();
    }

    public void RemoveItem(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            Items.RemoveAt(index);
            if (CurrentIndex > index) CurrentIndex--;
            else if (CurrentIndex >= Items.Count) CurrentIndex = Items.Count - 1;
            Persist();
        }
    }

    public void ClearUpNext()
    {
        UpNext.Clear();
        if (CurrentIndex >= 0 && CurrentIndex < Items.Count - 1)
        {
            Items.RemoveRange(CurrentIndex + 1, Items.Count - (CurrentIndex + 1));
        }
        Persist();
    }

    private void ShuffleUnlockedItems()
    {
        var unlocked = Items.Where(i => !i.Locked).ToList();
        ShuffleInPlace(unlocked);
        var next = 0;
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Locked) continue;
            Items[i] = unlocked[next++];
        }
    }

    private void SortUpNextByPriority()
    {
        if (!UpNext.Exists(i => i.Priority is not null)) return;
        UpNext.Sort((a, b) => (b.Priority ?? 0).CompareTo(a.Priority ?? 0));
    }

    private static void MoveList<T>(List<T> list, int from, int to)
    {
        var toOffset = to > from ? to + 1 : to;
        var item = list[from];
        list.RemoveAt(from);
        var insert = toOffset;
        if (from < toOffset) insert--;
        insert = Math.Clamp(insert, 0, list.Count);
        list.Insert(insert, item);
    }

    private static void Replace<T>(List<T> target, IEnumerable<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private static T? Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch { return default; }
    }

    private static void ShuffleInPlace<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
