using System.Collections.Concurrent;
using Muses.Core.Catalog;
using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Infrastructure.YTDlp;

namespace Muses.Infrastructure.Catalog;

public sealed class YouTubeCatalogService
{
    private readonly ICatalogRepository _catalogRepo;
    private readonly ITrackRepository _trackRepo;
    private readonly IYTDlpBridge? _bridge;

    private readonly ConcurrentDictionary<string, ArtistOnlineDiscography> _discographyCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<YTDlpPlaylistEntry>> _albumTracksCache = new(StringComparer.Ordinal);

    public int Revision { get; private set; }
    public event Action? Changed;

    public YouTubeCatalogService(
        ICatalogRepository catalogRepo,
        ITrackRepository trackRepo,
        IYTDlpBridge? bridge = null)
    {
        _catalogRepo = catalogRepo;
        _trackRepo = trackRepo;
        _bridge = bridge;
    }

    public void RebuildFromTrackMetadata()
    {
        var tracks = _trackRepo.All().Where(t => !string.IsNullOrEmpty(t.YouTubeId)).ToList();
        var didModifyTracks = false;

        foreach (var track in tracks)
        {
            if (track.ReleaseCatalogId is not null && track.ReleaseCatalogId.StartsWith("playlist:", StringComparison.Ordinal))
            {
                var pid = track.ReleaseCatalogId["playlist:".Length..];
                if (!YouTubePlaylistID.IsMusicAlbum(pid))
                {
                    track.ReleaseCatalogId = null;
                    didModifyTracks = true;
                }
            }

            var rawArtist = (track.Artist ?? "").Trim();
            var fallbackArtist = (track.AlbumArtist ?? "").Trim();
            var cleanArtist = !string.IsNullOrEmpty(rawArtist) ? rawArtist :
                (!string.IsNullOrEmpty(fallbackArtist) ? fallbackArtist : "Unknown Artist");

            if (track.ArtistCatalogId is null)
            {
                track.ArtistCatalogId = $"artist:{cleanArtist.ToLowerInvariant()}";
                didModifyTracks = true;
            }

            if (track.ReleaseCatalogId is null)
            {
                var artistKey = cleanArtist.ToLowerInvariant();
                var albumTitle = track.AlbumTitle?.Trim();
                if (!string.IsNullOrEmpty(albumTitle))
                {
                    track.ReleaseCatalogId = $"album:{artistKey}:{albumTitle.ToLowerInvariant()}";
                    didModifyTracks = true;
                }
                else
                {
                    track.ReleaseCatalogId = $"single:{track.YouTubeId}";
                    if (string.IsNullOrEmpty(track.AlbumTitle))
                    {
                        track.AlbumTitle = track.Title;
                    }
                    didModifyTracks = true;
                }
            }

            if (didModifyTracks)
            {
                _trackRepo.Upsert(track);
            }
        }

        var releaseGroups = tracks.Where(t => t.ReleaseCatalogId is not null)
            .GroupBy(t => t.ReleaseCatalogId!, StringComparer.Ordinal);

        var liveReleaseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in releaseGroups)
        {
            var stableId = group.Key;
            liveReleaseIds.Add(stableId);

            var members = group.ToList();
            var first = members[0];
            var releaseTitle = !string.IsNullOrEmpty(first.AlbumTitle) ? first.AlbumTitle : first.Title;
            var artistName = !string.IsNullOrEmpty(first.AlbumArtist) ? first.AlbumArtist : first.Artist;
            var artistStableIds = members.Select(m => m.ArtistCatalogId).Where(id => id is not null).Distinct().ToList();
            var artistStableId = artistStableIds.Count == 1 ? artistStableIds[0] : null;
            var artworkUrl = first.ArtworkUrl ?? members.Select(m => m.ArtworkUrl).FirstOrDefault(u => u is not null);
            var year = members.Select(m => m.Year).FirstOrDefault(y => y is not null);

            var kind = CatalogReleaseKind.Album;
            if (stableId.StartsWith("single:", StringComparison.Ordinal))
            {
                kind = CatalogReleaseKind.Single;
            }
            else
            {
                var tLower = releaseTitle.ToLowerInvariant();
                if (tLower.Contains(" - single") || tLower.Contains("(single)")) kind = CatalogReleaseKind.Single;
                else if (tLower.Contains(" - ep") || tLower.Contains("(ep)")) kind = CatalogReleaseKind.Ep;
                else if (members.Count == 1 && (string.IsNullOrEmpty(first.AlbumTitle) || first.AlbumTitle == first.Title)) kind = CatalogReleaseKind.Single;
            }

            var existing = _catalogRepo.GetRelease(stableId);
            _catalogRepo.UpsertRelease(new CatalogReleaseEntity
            {
                StableId = stableId,
                Title = releaseTitle,
                ArtistName = artistName,
                ArtistStableId = artistStableId ?? existing?.ArtistStableId,
                ArtworkUrl = artworkUrl ?? existing?.ArtworkUrl,
                Year = year ?? existing?.Year,
                Kind = existing is not null && existing.Kind != CatalogReleaseKind.Unknown ? existing.Kind : kind,
                RefreshedAt = DateTimeOffset.UtcNow,
                Unavailable = false
            });
        }
        _catalogRepo.DeleteOrphanReleases(liveReleaseIds);

        var artistGroups = tracks.Where(t => t.ArtistCatalogId is not null)
            .GroupBy(t => t.ArtistCatalogId!, StringComparer.Ordinal);

        var liveArtistIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in artistGroups)
        {
            var stableId = group.Key;
            liveArtistIds.Add(stableId);

            var members = group.ToList();
            var first = members[0];
            var rawArtist = (first.Artist ?? "").Trim();
            var fallbackArtist = (first.AlbumArtist ?? "").Trim();
            var artistName = !string.IsNullOrEmpty(rawArtist) ? rawArtist :
                (!string.IsNullOrEmpty(fallbackArtist) ? fallbackArtist : "Unknown Artist");

            string? channelId = stableId.StartsWith("channel:", StringComparison.Ordinal)
                ? stableId["channel:".Length..] : null;
            var artworkUrl = first.ArtworkUrl ?? members.Select(m => m.ArtworkUrl).FirstOrDefault(u => u is not null);

            var existing = _catalogRepo.GetArtist(stableId);
            _catalogRepo.UpsertArtist(new CatalogArtistEntity
            {
                StableId = stableId,
                Name = artistName,
                ChannelId = channelId ?? existing?.ChannelId,
                ArtworkUrl = artworkUrl ?? existing?.ArtworkUrl,
                RefreshedAt = DateTimeOffset.UtcNow,
                Unavailable = false
            });
        }
        _catalogRepo.DeleteOrphanArtists(liveArtistIds);

        Revision++;
        Changed?.Invoke();
    }

    public IReadOnlyList<CatalogReleaseProjection> Releases(DateTimeOffset? now = null)
    {
        var tracks = _trackRepo.All().Where(t => !string.IsNullOrEmpty(t.YouTubeId)).ToList();
        var releaseRows = _catalogRepo.GetReleases();

        var hasInvalidPlaylistReleases = tracks.Any(t =>
            t.ReleaseCatalogId is not null &&
            t.ReleaseCatalogId.StartsWith("playlist:", StringComparison.Ordinal) &&
            !YouTubePlaylistID.IsMusicAlbum(t.ReleaseCatalogId["playlist:".Length..]));

        var uncataloged = tracks.Any(t => t.ReleaseCatalogId is null);

        if ((releaseRows.Count == 0 && tracks.Count > 0) || uncataloged || hasInvalidPlaylistReleases)
        {
            RebuildFromTrackMetadata();
            tracks = _trackRepo.All().Where(t => !string.IsNullOrEmpty(t.YouTubeId)).ToList();
            releaseRows = _catalogRepo.GetReleases();
        }

        var grouped = tracks.Where(t => t.ReleaseCatalogId is not null)
            .GroupBy(t => t.ReleaseCatalogId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var list = new List<CatalogReleaseProjection>();
        var currentTime = now ?? DateTimeOffset.UtcNow;

        foreach (var row in releaseRows)
        {
            if (!grouped.TryGetValue(row.StableId, out var members) || members.Count == 0) continue;

            var ordered = members
                .OrderBy(m => m.ReleaseOrder ?? int.MaxValue)
                .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Id)
                .Select(m => m.ToSnapshot())
                .ToList();

            list.Add(new CatalogReleaseProjection(
                row.StableId,
                row.Title,
                row.ArtistName ?? "Unknown Artist",
                row.ArtistStableId,
                row.ArtworkUrl,
                row.Year,
                row.Kind,
                CatalogCacheStateResolver.Resolve(row.RefreshedAt, row.Unavailable, currentTime),
                ordered));
        }

        return list.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<CatalogArtistProjection> Artists(DateTimeOffset? now = null)
    {
        var tracks = _trackRepo.All().Where(t => !string.IsNullOrEmpty(t.YouTubeId)).ToList();
        var artistRows = _catalogRepo.GetArtists();

        var uncataloged = tracks.Any(t => t.ArtistCatalogId is null);
        if ((artistRows.Count == 0 && tracks.Count > 0) || uncataloged)
        {
            RebuildFromTrackMetadata();
            tracks = _trackRepo.All().Where(t => !string.IsNullOrEmpty(t.YouTubeId)).ToList();
            artistRows = _catalogRepo.GetArtists();
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var allReleases = Releases(now);
        var grouped = tracks.Where(t => t.ArtistCatalogId is not null)
            .GroupBy(t => t.ArtistCatalogId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var list = new List<CatalogArtistProjection>();
        foreach (var row in artistRows)
        {
            var artistTracks = (grouped.GetValueOrDefault(row.StableId) ?? [])
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Id)
                .Select(t => t.ToSnapshot())
                .ToList();

            var artistReleases = allReleases.Where(r => r.ArtistStableId == row.StableId).ToList();

            list.Add(new CatalogArtistProjection(
                row.StableId,
                row.Name,
                row.ArtworkUrl,
                row.Biography,
                CatalogCacheStateResolver.Resolve(row.RefreshedAt, row.Unavailable, currentTime),
                artistReleases,
                artistTracks));
        }

        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public CatalogReleaseProjection? Release(string stableId) =>
        Releases().FirstOrDefault(r => string.Equals(r.StableId, stableId, StringComparison.Ordinal));

    public CatalogArtistProjection? Artist(string stableId) =>
        Artists().FirstOrDefault(a => string.Equals(a.StableId, stableId, StringComparison.Ordinal));

    public async Task<ArtistOnlineDiscography> FetchArtistOnlineDiscographyAsync(
        CatalogArtistProjection artist,
        CancellationToken ct = default)
    {
        if (_discographyCache.TryGetValue(artist.StableId, out var cached))
            return cached;

        if (_bridge is null)
            return new ArtistOnlineDiscography(artist.Name);

        string? channelId = artist.StableId.StartsWith("channel:", StringComparison.Ordinal)
            ? artist.StableId["channel:".Length..] : null;

        IReadOnlyList<YTDlpPlaylistEntry> topSongs;
        try
        {
            topSongs = await _bridge.SearchAsync($"{artist.Name} official audio", 12, TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
        }
        catch
        {
            topSongs = Array.Empty<YTDlpPlaylistEntry>();
        }

        IReadOnlyList<YTDlpPlaylistEntry> rawReleases = Array.Empty<YTDlpPlaylistEntry>();
        if (!string.IsNullOrEmpty(channelId))
        {
            try
            {
                var releasesUrl = $"https://www.youtube.com/channel/{channelId}/releases";
                var entries = await _bridge.FetchPlaylistAsync(releasesUrl, TimeSpan.FromSeconds(35), ct).ConfigureAwait(false);
                if (entries.Count > 0) rawReleases = entries;
            }
            catch
            {
                // Fall through to search
            }
        }

        if (rawReleases.Count == 0)
        {
            try
            {
                rawReleases = await _bridge.SearchAsync($"{artist.Name} album", 12, TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
            }
            catch
            {
                rawReleases = Array.Empty<YTDlpPlaylistEntry>();
            }
        }

        var albums = new List<OnlineReleaseItem>();
        var singlesAndEPs = new List<OnlineReleaseItem>();

        foreach (var entry in rawReleases)
        {
            var titleLower = entry.Title.ToLowerInvariant();
            var isSingleOrEp = titleLower.Contains(" - single")
                || titleLower.Contains(" - ep")
                || titleLower.Contains("(single)")
                || titleLower.Contains("(ep)")
                || titleLower.Contains("remix");

            var kind = isSingleOrEp ? CatalogReleaseKind.Single : CatalogReleaseKind.Album;
            var item = new OnlineReleaseItem(
                entry.Id,
                entry.Title,
                $"https://i.ytimg.com/vi/{entry.Id}/hqdefault.jpg",
                entry.ReleaseYear,
                kind,
                channelId ?? entry.ChannelId);

            if (isSingleOrEp) singlesAndEPs.Add(item);
            else albums.Add(item);
        }

        var topSnapshots = topSongs.Select(e => new TrackSnapshot(
            Guid.NewGuid(),
            e.Title,
            artist.Name,
            e.Album,
            e.Duration ?? 0,
            e.Id,
            $"https://i.ytimg.com/vi/{e.Id}/hqdefault.jpg")).ToList();

        var discography = new ArtistOnlineDiscography(
            artist.Name,
            channelId,
            topSnapshots,
            albums,
            singlesAndEPs);

        _discographyCache[artist.StableId] = discography;
        return discography;
    }

    public async Task<IReadOnlyList<YTDlpPlaylistEntry>> FetchAlbumOnlineTracksAsync(
        CatalogReleaseProjection release,
        CancellationToken ct = default)
    {
        if (_albumTracksCache.TryGetValue(release.StableId, out var cached))
            return cached;

        if (_bridge is null) return Array.Empty<YTDlpPlaylistEntry>();

        string? targetUrl = null;
        if (release.StableId.StartsWith("playlist:", StringComparison.Ordinal))
        {
            var pid = release.StableId["playlist:".Length..];
            targetUrl = $"https://www.youtube.com/playlist?list={pid}";
        }
        else if (release.StableId.StartsWith("browse:", StringComparison.Ordinal))
        {
            var bid = release.StableId["browse:".Length..];
            targetUrl = $"https://music.youtube.com/browse/{bid}";
        }

        IReadOnlyList<YTDlpPlaylistEntry> entries;
        if (targetUrl is not null)
        {
            try
            {
                entries = await _bridge.FetchPlaylistAsync(targetUrl, TimeSpan.FromSeconds(40), ct).ConfigureAwait(false);
            }
            catch
            {
                entries = Array.Empty<YTDlpPlaylistEntry>();
            }
        }
        else
        {
            try
            {
                var query = $"{release.ArtistName} {release.Title}";
                entries = await _bridge.SearchAsync(query, 20, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
            catch
            {
                entries = Array.Empty<YTDlpPlaylistEntry>();
            }
        }

        _albumTracksCache[release.StableId] = entries;
        return entries;
    }

    public TrackSnapshot ImportOnlineTrack(
        YTDlpPlaylistEntry entry,
        string? releaseStableId = null,
        int? order = null,
        string? albumTitle = null,
        string? artistName = null)
    {
        var existing = _trackRepo.GetByYouTubeId(entry.Id);
        TrackEntity track;

        if (existing is not null)
        {
            track = existing;
            if (track.ReleaseCatalogId is null && releaseStableId is not null)
            {
                track.ReleaseCatalogId = releaseStableId;
                track.ReleaseOrder = order;
            }
            if (track.AlbumTitle is null && albumTitle is not null)
            {
                track.AlbumTitle = albumTitle;
            }
            _trackRepo.Upsert(track);
        }
        else
        {
            var resolvedArtist = artistName ?? entry.Uploader ?? "Unknown";
            var artistStableId = YouTubeCatalogIdentity.Artist(entry.ChannelId, null)
                ?? $"artist:{resolvedArtist.ToLowerInvariant()}";

            track = new TrackEntity
            {
                Title = entry.Title,
                Artist = resolvedArtist,
                AlbumTitle = albumTitle ?? entry.Album,
                AlbumArtist = resolvedArtist,
                DurationMs = (int)Math.Round((entry.Duration ?? 0) * 1000),
                YouTubeId = entry.Id,
                ArtworkUrl = $"https://i.ytimg.com/vi/{entry.Id}/hqdefault.jpg",
                ReleaseCatalogId = releaseStableId,
                ReleaseOrder = order,
                ArtistCatalogId = artistStableId
            };
            _trackRepo.Upsert(track);

            if (_catalogRepo.GetArtist(artistStableId) is null)
            {
                _catalogRepo.UpsertArtist(new CatalogArtistEntity
                {
                    StableId = artistStableId,
                    Name = resolvedArtist,
                    ChannelId = entry.ChannelId,
                    RefreshedAt = DateTimeOffset.UtcNow
                });
            }
        }

        Revision++;
        Changed?.Invoke();
        return track.ToSnapshot();
    }

    public void ImportOnlineAlbum(
        OnlineReleaseItem release,
        IReadOnlyList<YTDlpPlaylistEntry> tracks,
        string? artistName = null)
    {
        var releaseStableId = release.StableId;
        var albumTitle = release.Title;
        var artist = artistName ?? "Unknown";

        var existingRelease = _catalogRepo.GetRelease(releaseStableId) ?? new CatalogReleaseEntity
        {
            StableId = releaseStableId,
            Title = albumTitle,
            ArtistName = artist,
            ArtistStableId = release.ChannelId is not null ? $"channel:{release.ChannelId}" : null,
            ArtworkUrl = release.ArtworkUrl,
            Year = release.Year,
            Kind = release.Kind,
            RefreshedAt = DateTimeOffset.UtcNow
        };
        _catalogRepo.UpsertRelease(existingRelease);

        for (var i = 0; i < tracks.Count; i++)
        {
            ImportOnlineTrack(tracks[i], releaseStableId, i + 1, albumTitle, artist);
        }

        Revision++;
        Changed?.Invoke();
    }
}
