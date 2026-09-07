using Muses.Core.Catalog;
using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Search;
using Muses.Infrastructure.Catalog;
using Muses.Infrastructure.YTDlp;

namespace Muses.Infrastructure.Search;

public sealed class GlobalSearchService
{
    private readonly LibraryService _library;
    private readonly YouTubeCatalogService? _catalog;
    private readonly IYTDlpBridge? _bridge;
    private readonly int _debounceMs;

    private string _query = "";
    private GlobalSearchScope _scope = GlobalSearchScope.All;
    private CancellationTokenSource? _searchCts;

    public event Action? Changed;

    public string Query
    {
        get => _query;
        set
        {
            if (_query == value) return;
            _query = value;
            ScheduleSearch();
            Changed?.Invoke();
        }
    }

    public GlobalSearchScope Scope
    {
        get => _scope;
        set
        {
            if (_scope == value) return;
            _scope = value;
            ClearResultsExcludedByScope(_scope);
            ScheduleSearch();
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<TrackSnapshot> TrackResults { get; private set; } = Array.Empty<TrackSnapshot>();
    public IReadOnlyList<CatalogReleaseProjection> ReleaseResults { get; private set; } = Array.Empty<CatalogReleaseProjection>();
    public IReadOnlyList<CatalogArtistProjection> CatalogArtistResults { get; private set; } = Array.Empty<CatalogArtistProjection>();
    public IReadOnlyList<YTDlpPlaylistEntry> YouTubeResults { get; private set; } = Array.Empty<YTDlpPlaylistEntry>();
    public bool IsSearchingYouTube { get; private set; }

    public bool HasResults =>
        TrackResults.Count > 0 ||
        ReleaseResults.Count > 0 ||
        CatalogArtistResults.Count > 0 ||
        YouTubeResults.Count > 0;

    public GlobalSearchService(
        LibraryService library,
        YouTubeCatalogService? catalog = null,
        IYTDlpBridge? bridge = null,
        int debounceMs = 250)
    {
        _library = library;
        _catalog = catalog;
        _bridge = bridge;
        _debounceMs = debounceMs;
    }

    public void Reset()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _query = "";
        _scope = GlobalSearchScope.All;
        ClearAllResults();
        Changed?.Invoke();
    }

    private void ScheduleSearch()
    {
        _searchCts?.Cancel();
        var trimmed = _query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ClearAllResults();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceMs, cts.Token).ConfigureAwait(false);
                if (cts.Token.IsCancellationRequested) return;
                await PerformSearchAsync(trimmed, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
        });
    }

    public async Task PerformSearchAsync(string query, CancellationToken ct = default)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ClearAllResults();
            Changed?.Invoke();
            return;
        }

        var requestedScope = _scope;
        if (requestedScope != GlobalSearchScope.YouTube)
        {
            var tracks = _library.SearchTracks(trimmed)
                .Where(t => !string.IsNullOrEmpty(t.YouTubeId))
                .Select(t => t.ToSnapshot())
                .ToList();
            TrackResults = tracks;

            if (_catalog is not null)
            {
                var releases = _catalog.Releases();
                ReleaseResults = releases.Where(r =>
                    r.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    r.ArtistName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

                var artists = _catalog.Artists();
                CatalogArtistResults = artists.Where(a =>
                    a.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                ReleaseResults = Array.Empty<CatalogReleaseProjection>();
                CatalogArtistResults = Array.Empty<CatalogArtistProjection>();
            }
        }
        else
        {
            ClearLibraryResults();
        }

        if (requestedScope != GlobalSearchScope.Library && _bridge is not null)
        {
            IsSearchingYouTube = true;
            Changed?.Invoke();
            try
            {
                var yt = await _bridge.SearchAsync(trimmed, 16, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested && _scope == requestedScope)
                {
                    YouTubeResults = yt;
                }
            }
            catch
            {
                if (!ct.IsCancellationRequested && _scope == requestedScope)
                {
                    YouTubeResults = Array.Empty<YTDlpPlaylistEntry>();
                }
            }
            finally
            {
                IsSearchingYouTube = false;
                Changed?.Invoke();
            }
        }
        else
        {
            YouTubeResults = Array.Empty<YTDlpPlaylistEntry>();
            IsSearchingYouTube = false;
            Changed?.Invoke();
        }
    }

    private void ClearResultsExcludedByScope(GlobalSearchScope scope)
    {
        if (scope == GlobalSearchScope.YouTube) ClearLibraryResults();
        if (scope == GlobalSearchScope.Library)
        {
            YouTubeResults = Array.Empty<YTDlpPlaylistEntry>();
            IsSearchingYouTube = false;
        }
    }

    private void ClearLibraryResults()
    {
        TrackResults = Array.Empty<TrackSnapshot>();
        ReleaseResults = Array.Empty<CatalogReleaseProjection>();
        CatalogArtistResults = Array.Empty<CatalogArtistProjection>();
    }

    private void ClearAllResults()
    {
        ClearLibraryResults();
        YouTubeResults = Array.Empty<YTDlpPlaylistEntry>();
        IsSearchingYouTube = false;
    }
}
