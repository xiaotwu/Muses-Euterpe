using Muses.Core.Domain;

namespace Muses.Core.Library;

public sealed class PlaylistService
{
    private readonly IPlaylistRepository _repository;
    public event Action? PlaylistsChanged;

    public PlaylistService(IPlaylistRepository repository)
    {
        _repository = repository;
    }

    public PlaylistEntity Create(string name)
    {
        var playlist = _repository.Create(name.Trim());
        PlaylistsChanged?.Invoke();
        return playlist;
    }

    public void Rename(PlaylistEntity playlist, string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        _repository.Rename(playlist.Id, trimmed);
        playlist.Name = trimmed;
        PlaylistsChanged?.Invoke();
    }

    public void Delete(PlaylistEntity playlist)
    {
        _repository.Delete(playlist.Id);
        PlaylistsChanged?.Invoke();
    }

    public PlaylistDeletionSnapshot? DeleteWithUndoSnapshot(PlaylistEntity playlist)
    {
        var snapshot = _repository.DeleteWithUndoSnapshot(playlist.Id);
        if (snapshot is not null)
        {
            PlaylistsChanged?.Invoke();
        }
        return snapshot;
    }

    public PlaylistEntity? Restore(PlaylistDeletionSnapshot snapshot)
    {
        var restored = _repository.Restore(snapshot);
        if (restored is not null)
        {
            PlaylistsChanged?.Invoke();
        }
        return restored;
    }

    public IReadOnlyList<PlaylistEntity> FetchAll() => _repository.GetAll();

    public PlaylistEntity? Get(Guid id) => _repository.Get(id);

    public void AddTrack(PlaylistEntity playlist, TrackEntity track)
    {
        _repository.AddTrack(playlist.Id, track.Id);
        PlaylistsChanged?.Invoke();
    }

    public void RemoveItem(PlaylistItemEntity item)
    {
        _repository.RemoveItem(item.Id);
        PlaylistsChanged?.Invoke();
    }

    public void RemoveItem(Guid itemId)
    {
        _repository.RemoveItem(itemId);
        PlaylistsChanged?.Invoke();
    }

    public void MoveItem(PlaylistEntity playlist, int from, int to)
    {
        _repository.MoveItem(playlist.Id, from, to);
        PlaylistsChanged?.Invoke();
    }

    public void TogglePin(PlaylistEntity playlist)
    {
        _repository.TogglePin(playlist.Id);
        playlist.Pinned = !playlist.Pinned;
        PlaylistsChanged?.Invoke();
    }

    public void TogglePin(Guid playlistId)
    {
        _repository.TogglePin(playlistId);
        PlaylistsChanged?.Invoke();
    }

    public PlaylistDeletionSnapshot? DeleteWithUndoSnapshot(Guid playlistId)
    {
        var snapshot = _repository.DeleteWithUndoSnapshot(playlistId);
        if (snapshot is not null)
        {
            PlaylistsChanged?.Invoke();
        }
        return snapshot;
    }

    public IReadOnlyList<PlaylistEntity> PinnedPlaylists() => _repository.GetPinned();
}
