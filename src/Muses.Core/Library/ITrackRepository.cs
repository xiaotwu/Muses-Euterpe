using Muses.Core.Domain;

namespace Muses.Core.Library;

public sealed class TrackEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? AlbumTitle { get; set; }
    public string? AlbumArtist { get; set; }
    public int DurationMs { get; set; }
    public int? TrackNo { get; set; }
    public int? DiscNo { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public string YouTubeId { get; set; } = "";
    public string? MediaKindRaw { get; set; }
    public string? ReleaseCatalogId { get; set; }
    public int? ReleaseOrder { get; set; }
    public string? ArtistCatalogId { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? Lyrics { get; set; }
    public int? LyricsOffsetMs { get; set; }
    public double? ReplayGain { get; set; }
    public int? SampleRate { get; set; }
    public int? BitDepth { get; set; }
    public string? Codec { get; set; }
    public int? BitRate { get; set; }
    public int? Channels { get; set; }
    public bool IsLossless { get; set; }
    public string MetadataStatusRaw { get; set; } = "embedded";
    public string AvailabilityRaw { get; set; } = "available";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; set; }
    public int PlayCount { get; set; }
    public bool Liked { get; set; }
    public bool LibraryMember { get; set; } = true;

    public TrackSnapshot ToSnapshot() => new(
        Id, Title, Artist, AlbumTitle, DurationMs / 1000.0, YouTubeId, ArtworkUrl,
        SampleRate, BitDepth, Codec, IsLossless, Liked, Lyrics, ReplayGain, BitRate, Channels, LyricsOffsetMs)
    {
        MediaKind = TrackMediaKindCodec.Parse(MediaKindRaw)
    };

    public static TrackEntity FromSnapshot(TrackSnapshot snap) => new()
    {
        Id = snap.Id,
        Title = snap.Title,
        Artist = snap.Artist,
        AlbumTitle = snap.AlbumTitle,
        DurationMs = (int)Math.Round(snap.DurationSeconds * 1000),
        YouTubeId = snap.YouTubeId,
        MediaKindRaw = TrackMediaKindCodec.Wire(snap.MediaKind),
        ArtworkUrl = snap.ArtworkUrl,
        SampleRate = snap.SampleRate,
        BitDepth = snap.BitDepth,
        Codec = snap.Codec,
        IsLossless = snap.IsLossless,
        Liked = snap.Liked,
        Lyrics = snap.Lyrics,
        ReplayGain = snap.ReplayGain,
        BitRate = snap.BitRate,
        Channels = snap.Channels,
        LyricsOffsetMs = snap.LyricsOffsetMs
    };
}

public interface ITrackRepository
{
    void Upsert(TrackEntity track);
    TrackEntity? Get(Guid id);
    TrackEntity? GetByYouTubeId(string youTubeId);
    IReadOnlyList<TrackEntity> All();
    void RecordPlay(Guid id);
    void ToggleLike(Guid id);
}

public sealed class LibraryService
{
    private readonly ITrackRepository _tracks;
    public int PlayRevision { get; private set; }
    public event Action? Changed;

    public LibraryService(ITrackRepository tracks) => _tracks = tracks;

    public void NotifyLibraryChanged() => Changed?.Invoke();

    public IReadOnlyList<TrackEntity> AllTracks() => _tracks.All().Where(t => t.LibraryMember).ToList();

    public IReadOnlyList<TrackEntity> LikedTracks() => _tracks.All().Where(t => t.Liked).ToList();

    public IReadOnlyList<TrackEntity> SearchTracks(string query)
    {
        var tracks = AllTracks();
        if (string.IsNullOrWhiteSpace(query)) return tracks;
        var q = query.Trim();
        return tracks.Where(t =>
            t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (t.AlbumTitle is not null && t.AlbumTitle.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public TrackEntity? TrackByYouTubeId(string videoId) => _tracks.GetByYouTubeId(videoId);

    public TrackEntity? Track(Guid id) => _tracks.Get(id);
    public TrackEntity? Get(Guid id) => _tracks.Get(id);
    public void ToggleLike(Guid id)
    {
        _tracks.ToggleLike(id);
        Changed?.Invoke();
    }

    /// <summary>Resolves a persistent playback identity. Only explicit saves add it to the library.</summary>
    public TrackSnapshot UpsertFromYouTube(
        string videoId, string title, string artist, double durationSeconds, string? artworkUrl,
        bool saveToLibrary = false, string? albumTitle = null, TrackMediaKind? mediaKind = null)
    {
        var durationMs = durationSeconds is > 0 and <= int.MaxValue / 1000d
            ? (int)Math.Round(durationSeconds * 1000)
            : 0;
        var mediaKindRaw = mediaKind is { } kind ? TrackMediaKindCodec.Wire(kind) : null;
        var existing = _tracks.GetByYouTubeId(videoId);
        if (existing is not null)
        {
            var enrichedTitle = string.IsNullOrWhiteSpace(existing.Title) || existing.Title == videoId
                ? title : existing.Title;
            var enrichedArtist = string.IsNullOrWhiteSpace(existing.Artist) ? artist : existing.Artist;
            var enrichedDuration = existing.DurationMs > 0 ? existing.DurationMs : durationMs;
            var enrichedArtwork = existing.ArtworkUrl ?? artworkUrl;
            var enrichedAlbum = string.IsNullOrWhiteSpace(existing.AlbumTitle) ? albumTitle ?? existing.AlbumTitle : existing.AlbumTitle;
            var enrichedKind = mediaKindRaw ?? existing.MediaKindRaw;
            var isMember = existing.LibraryMember || saveToLibrary;
            if (enrichedTitle != existing.Title || enrichedArtist != existing.Artist ||
                enrichedDuration != existing.DurationMs || enrichedArtwork != existing.ArtworkUrl ||
                enrichedAlbum != existing.AlbumTitle || enrichedKind != existing.MediaKindRaw ||
                isMember != existing.LibraryMember)
            {
                var addedToLibrary = isMember && !existing.LibraryMember;
                existing.Title = enrichedTitle;
                existing.Artist = enrichedArtist;
                existing.DurationMs = enrichedDuration;
                existing.ArtworkUrl = enrichedArtwork;
                existing.AlbumTitle = enrichedAlbum;
                existing.MediaKindRaw = enrichedKind;
                existing.LibraryMember = isMember;
                _tracks.Upsert(existing);
                if (addedToLibrary) Changed?.Invoke();
            }
            return existing.ToSnapshot();
        }

        var entity = new TrackEntity
        {
            Title = title,
            Artist = artist,
            DurationMs = durationMs,
            YouTubeId = videoId,
            MediaKindRaw = mediaKindRaw,
            ArtworkUrl = artworkUrl,
            AlbumTitle = albumTitle,
            LibraryMember = saveToLibrary
        };
        _tracks.Upsert(entity);
        if (saveToLibrary) Changed?.Invoke();
        return entity.ToSnapshot();
    }

    public TrackSnapshot Save(TrackSnapshot track) => UpsertFromYouTube(
        track.YouTubeId, track.Title, track.Artist, track.DurationSeconds, track.ArtworkUrl,
        saveToLibrary: true, albumTitle: track.AlbumTitle, mediaKind: track.MediaKind);

    public void RecordPlay(Guid trackId)
    {
        _tracks.RecordPlay(trackId);
        PlayRevision++;
        Changed?.Invoke();
    }

    public IReadOnlyList<TrackEntity> RecentlyPlayedTracks(int limit)
    {
        return _tracks.All()
            .Where(t => t.LastPlayedAt is not null && !string.IsNullOrEmpty(t.YouTubeId))
            .GroupBy(t => t.YouTubeId)
            .Select(g => g.OrderByDescending(t => t.LastPlayedAt).First())
            .OrderByDescending(t => t.LastPlayedAt)
            .Take(limit)
            .ToList();
    }
}

