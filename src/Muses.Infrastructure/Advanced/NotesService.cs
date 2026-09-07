using Muses.Core.Advanced;

namespace Muses.Infrastructure.Advanced;

public sealed class NotesService
{
    private readonly INotesRepository _repository;
    public event Action? Changed;

    public NotesService(INotesRepository repository)
    {
        _repository = repository;
    }

    public TrackNoteEntity? GetNote(string trackId) => _repository.GetNote(trackId);

    public void SetTrackNote(string trackId, string content)
    {
        _repository.SetNote(trackId, content);
        Changed?.Invoke();
    }

    public IReadOnlyList<TrackBookmarkEntity> GetBookmarks(string trackId) =>
        _repository.GetBookmarks(trackId);

    public TrackBookmarkEntity AddBookmark(string trackId, double timestampMs, string? title = null, string? note = null)
    {
        var bm = new TrackBookmarkEntity(
            Guid.NewGuid().ToString(),
            trackId,
            timestampMs,
            title,
            note,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _repository.AddBookmark(bm);
        Changed?.Invoke();
        return bm;
    }

    public void UpdateBookmark(string id, string? title, string? note)
    {
        _repository.UpdateBookmark(id, title, note);
        Changed?.Invoke();
    }

    public void RemoveBookmark(string id)
    {
        _repository.DeleteBookmark(id);
        Changed?.Invoke();
    }

    public IReadOnlyList<NoteSearchHit> SearchNotes(string query, IReadOnlyDictionary<string, string> trackTitles) =>
        _repository.SearchNotes(query, trackTitles);
}
