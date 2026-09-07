using Muses.Core.Advanced;
using Muses.Core.Domain;
using Muses.Core.Playback;

namespace Muses.Infrastructure.Advanced;

public sealed class InboxService : IDisposable
{
    private readonly IInboxRepository _repository;
    private readonly PlaybackService _playback;

    public event Action? Changed;

    public InboxService(IInboxRepository repository, PlaybackService playback)
    {
        _repository = repository;
        _playback = playback;
        _playback.EventBus.EventPosted += OnPlaybackEvent;
    }

    private void OnPlaybackEvent(PlaybackEvent evt)
    {
        if (evt.Kind == PlaybackEventKind.TrackStarted && evt.Track != null)
        {
            MarkListening(evt.Track.Id.ToString());
        }
    }

    public IReadOnlyList<InboxItemEntity> GetAllItems() => _repository.GetItems();

    public IReadOnlyList<InboxItemEntity> GetPendingItems() =>
        _repository.GetItems().Where(i => i.State is InboxState.Unheard or InboxState.Listening).ToList();

    public IReadOnlyList<InboxItemEntity> GetSnoozedItems() =>
        _repository.GetItems().Where(i => i.State is InboxState.Snoozed).ToList();

    public IReadOnlyList<InboxItemEntity> GetDecidedItems() =>
        _repository.GetItems().Where(i => i.State is InboxState.Accepted or InboxState.Rejected).ToList();

    public InboxItemEntity? Add(TrackSnapshot snapshot, InboxSource source = InboxSource.Manual)
    {
        var existing = _repository.GetItems()
            .FirstOrDefault(i => i.TrackId == snapshot.Id.ToString() && i.State is not InboxState.Accepted and not InboxState.Rejected);
        if (existing != null) return null;

        var item = new InboxItemEntity(
            Guid.NewGuid().ToString(),
            snapshot.Id.ToString(),
            snapshot.Title,
            snapshot.Artist,
            snapshot.AlbumTitle,
            snapshot.DurationSeconds,
            snapshot.YouTubeId,
            snapshot.ArtworkUrl,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            source,
            InboxState.Unheard);

        _repository.SaveItem(item);
        Changed?.Invoke();
        return item;
    }

    public void Accept(string id, Action<string>? onLike = null)
    {
        var item = _repository.GetItems().FirstOrDefault(i => i.Id == id);
        if (item == null) return;

        _repository.UpdateState(id, InboxState.Accepted);
        if (!string.IsNullOrEmpty(item.TrackId))
        {
            onLike?.Invoke(item.TrackId);
        }
        Changed?.Invoke();
    }

    public void Reject(string id)
    {
        _repository.UpdateState(id, InboxState.Rejected);
        Changed?.Invoke();
    }

    public void Snooze(string id, DateTimeOffset until)
    {
        _repository.UpdateState(id, InboxState.Snoozed, until.ToUnixTimeMilliseconds());
        Changed?.Invoke();
    }

    public void AddNote(string id, string note)
    {
        _repository.UpdateNotes(id, note);
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        _repository.DeleteItem(id);
        Changed?.Invoke();
    }

    public void RestoreDueSnoozes(DateTimeOffset? now = null)
    {
        var targetNow = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        _repository.RestoreDueSnoozes(targetNow);
        Changed?.Invoke();
    }

    private void MarkListening(string trackId)
    {
        var item = _repository.GetItems()
            .FirstOrDefault(i => i.TrackId == trackId && i.State is InboxState.Unheard or InboxState.Snoozed);
        if (item != null)
        {
            _repository.UpdateState(item.Id, InboxState.Listening);
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _playback.EventBus.EventPosted -= OnPlaybackEvent;
    }
}
