using Muses.Core.Domain;

namespace Muses.Core.Library;

public interface IPlaylistRepository
{
    PlaylistEntity Create(string name);
    void Rename(Guid id, string newName);
    void Delete(Guid id);
    PlaylistDeletionSnapshot? DeleteWithUndoSnapshot(Guid id);
    PlaylistEntity? Restore(PlaylistDeletionSnapshot snapshot);
    IReadOnlyList<PlaylistEntity> GetAll();
    PlaylistEntity? Get(Guid id);
    void AddTrack(Guid playlistId, Guid trackId);
    void RemoveItem(Guid itemId);
    void MoveItem(Guid playlistId, int fromIndex, int toIndex);
    void TogglePin(Guid id);
    IReadOnlyList<PlaylistEntity> GetPinned();
}

public interface IYouTubeImportRepository
{
    void UpsertImport(YouTubeImportEntity imp);
    YouTubeImportEntity? GetImport(Guid id);
    YouTubeImportEntity? GetImportByPlaylistId(string playlistId);
    IReadOnlyList<YouTubeImportEntity> GetAllImports(bool includeDeleted = false);
    void MarkDeleted(Guid id);
    void Restore(Guid id);
    void HardDelete(Guid id);
    void RemoveImportItem(Guid importId, Guid itemId);
    void MoveImportItem(Guid importId, int fromIndex, int toIndex);
}
