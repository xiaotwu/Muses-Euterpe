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

    public TrackSnapshot ToSnapshot() => new(
        Id, Title, Artist, AlbumTitle, DurationMs / 1000.0, YouTubeId, ArtworkUrl,
        SampleRate, BitDepth, Codec, IsLossless, Liked, Lyrics, ReplayGain, BitRate, Channels, LyricsOffsetMs);

    public static TrackEntity FromSnapshot(TrackSnapshot snap) => new()
    {
        Id = snap.Id,
        Title = snap.Title,
        Artist = snap.Artist,
        AlbumTitle = snap.AlbumTitle,
        DurationMs = (int)Math.Round(snap.DurationSeconds * 1000),
        YouTubeId = snap.YouTubeId,
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

    public LibraryService(ITrackRepository tracks) => _tracks = tracks;

    public IReadOnlyList<TrackEntity> AllTracks() => _tracks.All();

    public IReadOnlyList<TrackEntity> LikedTracks() => _tracks.All().Where(t => t.Liked).ToList();

    public IReadOnlyList<TrackEntity> SearchTracks(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _tracks.All();
        var q = query.Trim();
        return _tracks.All().Where(t =>
            t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (t.AlbumTitle is not null && t.AlbumTitle.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public TrackEntity? TrackByYouTubeId(string videoId) => _tracks.GetByYouTubeId(videoId);

    public TrackEntity? Track(Guid id) => _tracks.Get(id);
    public TrackEntity? Get(Guid id) => _tracks.Get(id);
    public void ToggleLike(Guid id) => _tracks.ToggleLike(id);

    public TrackSnapshot UpsertFromYouTube(string videoId, string title, string artist, double durationSeconds, string? artworkUrl)
    {
        var existing = _tracks.GetByYouTubeId(videoId);
        if (existing is not null)
        {
            existing.Title = title;
            existing.Artist = artist;
            if (durationSeconds > 0) existing.DurationMs = (int)Math.Round(durationSeconds * 1000);
            existing.ArtworkUrl = artworkUrl ?? existing.ArtworkUrl;
            _tracks.Upsert(existing);
            return existing.ToSnapshot();
        }

        var entity = new TrackEntity
        {
            Title = title,
            Artist = artist,
            DurationMs = (int)Math.Round(durationSeconds * 1000),
            YouTubeId = videoId,
            ArtworkUrl = artworkUrl
        };
        _tracks.Upsert(entity);
        return entity.ToSnapshot();
    }

    public void RecordPlay(Guid trackId)
    {
        _tracks.RecordPlay(trackId);
        PlayRevision++;
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

