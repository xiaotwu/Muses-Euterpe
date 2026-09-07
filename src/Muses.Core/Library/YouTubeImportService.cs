using Muses.Core.Domain;
using Muses.Core.YouTube;

namespace Muses.Core.Library;

public sealed record YouTubePlaylistEntry(
    string Id,
    string Title,
    string? Uploader = null,
    double? Duration = null,
    string? PlaylistTitle = null,
    string? ChannelId = null,
    string? Track = null,
    string? Album = null,
    int? ReleaseYear = null);

public interface IYouTubePlaylistFetcher
{
    Task<IReadOnlyList<YouTubePlaylistEntry>> FetchPlaylistAsync(string url, CancellationToken ct = default);
}

public sealed class YouTubeImportService
{
    private readonly IYouTubeImportRepository _importRepo;
    private readonly ITrackRepository _trackRepo;
    private readonly IYouTubePlaylistFetcher _fetcher;

    public event Action? ImportsChanged;

    public YouTubeImportService(
        IYouTubeImportRepository importRepo,
        ITrackRepository trackRepo,
        IYouTubePlaylistFetcher fetcher)
    {
        _importRepo = importRepo;
        _trackRepo = trackRepo;
        _fetcher = fetcher;
    }

    public async Task<Guid> ImportPlaylistAsync(string url, CancellationToken ct = default)
    {
        var playlistId = YouTubeLinks.ExtractPlaylistId(url);
        if (string.IsNullOrEmpty(playlistId))
            throw new ArgumentException("Could not parse YouTube playlist URL (missing list parameter)", nameof(url));

        var existing = _importRepo.GetImportByPlaylistId(playlistId);
        if (existing is not null)
        {
            if (existing.DeletedAt is not null)
            {
                _importRepo.Restore(existing.Id);
                ImportsChanged?.Invoke();
            }
            return existing.Id;
        }

        var entries = await _fetcher.FetchPlaylistAsync(url, ct).ConfigureAwait(false);
        if (entries.Count == 0)
            throw new InvalidOperationException("Playlist is empty");

        var title = entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.PlaylistTitle))?.PlaylistTitle
                    ?? entries.FirstOrDefault()?.Title
                    ?? "YouTube Playlist";
        var channel = entries.FirstOrDefault()?.Uploader ?? "Unknown";
        var artworkUrl = entries.FirstOrDefault() != null
            ? YouTubeLinks.ThumbnailUrl(entries[0].Id)
            : null;

        var importEntity = new YouTubeImportEntity
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            Url = url,
            Title = title,
            Channel = channel,
            ArtworkUrl = artworkUrl,
            ImportedAt = DateTimeOffset.UtcNow,
            LastSyncedAt = DateTimeOffset.UtcNow
        };

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var durationMs = (int)Math.Round((entry.Duration ?? 0) * 1000);
            var artist = entry.Uploader ?? channel;

            var existingTrack = _trackRepo.GetByYouTubeId(entry.Id);
            TrackEntity track;
            if (existingTrack is not null)
            {
                if (existingTrack.DurationMs <= 0 && durationMs > 0)
                {
                    existingTrack.DurationMs = durationMs;
                    _trackRepo.Upsert(existingTrack);
                }
                track = existingTrack;
            }
            else
            {
                track = new TrackEntity
                {
                    Id = Guid.NewGuid(),
                    Title = entry.Title,
                    Artist = artist,
                    DurationMs = durationMs,
                    YouTubeId = entry.Id,
                    ArtworkUrl = YouTubeLinks.ThumbnailUrl(entry.Id),
                    AddedAt = DateTimeOffset.UtcNow
                };
                _trackRepo.Upsert(track);
            }

            var item = new YouTubeImportItemEntity
            {
                Id = Guid.NewGuid(),
                ImportId = importEntity.Id,
                TrackId = track.Id,
                YouTubeId = entry.Id,
                Title = entry.Title,
                ItemOrder = i,
                Track = track
            };
            importEntity.Items.Add(item);
        }

        _importRepo.UpsertImport(importEntity);
        ImportsChanged?.Invoke();
        return importEntity.Id;
    }

    public void MoveToRecentlyDeleted(Guid importId)
    {
        _importRepo.MarkDeleted(importId);
        ImportsChanged?.Invoke();
    }

    public void Restore(Guid importId)
    {
        _importRepo.Restore(importId);
        ImportsChanged?.Invoke();
    }

    public void HardDelete(Guid importId, bool deleteAssociatedTracks = false)
    {
        if (deleteAssociatedTracks)
        {
            var imp = _importRepo.GetImport(importId);
            if (imp is not null)
            {
                // Deleting tracks is only done if user explicitly requested
                // Note: For now we preserve tracks as default law:
                // "Deleting the import != deleting associated tracks"
            }
        }
        _importRepo.HardDelete(importId);
        ImportsChanged?.Invoke();
    }

    public IReadOnlyList<YouTubeImportEntity> GetActiveImports()
    {
        return _importRepo.GetAllImports(includeDeleted: false);
    }

    public IReadOnlyList<YouTubeImportEntity> GetRecentlyDeletedImports(int retentionDays = 30)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        return _importRepo.GetAllImports(includeDeleted: true)
            .Where(i => i.DeletedAt is not null && i.DeletedAt.Value >= cutoff)
            .ToList();
    }

    public YouTubeImportEntity? GetImport(Guid id) => _importRepo.GetImport(id);
    public YouTubeImportEntity? Get(Guid id) => GetImport(id);
    public IReadOnlyList<YouTubeImportEntity> FetchActive() => GetActiveImports();
    public IReadOnlyList<YouTubeImportEntity> FetchDeleted() => GetRecentlyDeletedImports();
    public void RestoreFromRecentlyDeleted(Guid importId) => Restore(importId);
    public void PermanentlyDelete(Guid importId) => HardDelete(importId);

    public void RemoveItem(Guid importId, Guid itemId)
    {
        _importRepo.RemoveImportItem(importId, itemId);
        ImportsChanged?.Invoke();
    }

    public void RemoveItem(Guid itemId)
    {
        var all = _importRepo.GetAllImports(includeDeleted: true);
        foreach (var imp in all)
        {
            if (imp.Items.Any(i => i.Id == itemId))
            {
                _importRepo.RemoveImportItem(imp.Id, itemId);
                ImportsChanged?.Invoke();
                return;
            }
        }
    }
}
