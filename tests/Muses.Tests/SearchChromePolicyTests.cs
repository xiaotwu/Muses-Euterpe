using Muses.Core.Library;
using Muses.Core.Search;
using Muses.Infrastructure.Catalog;
using Muses.Infrastructure.Search;
using Muses.Infrastructure.YTDlp;
using Muses.Persistence;

namespace Muses.Tests;

public class SearchChromePolicyTests
{
    private sealed class StubBridge : IYTDlpBridge
    {
        public Task<Uri> ResolveStreamUrlAsync(string videoId, string quality, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult(new Uri("https://example.com/stream"));

        public Task<IReadOnlyList<YTDlpPlaylistEntry>> FetchPlaylistAsync(string url, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<YTDlpPlaylistEntry>>([]);

        public Task<IReadOnlyList<YTDlpPlaylistEntry>> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<YTDlpPlaylistEntry>>([
                new YTDlpPlaylistEntry("yt_result_1", "YouTube Search Match: " + query, "Uploader")
            ]);

        public Task<YTDlpPlaylistEntry?> FetchVideoMetadataAsync(string videoId, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<YTDlpPlaylistEntry?>(null);

        public Task<string?> VersionAsync() => Task.FromResult<string?>("2024.01.01");
    }

    [Fact]
    public async Task Search_filters_by_scope()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var catalog = new YouTubeCatalogService(store, store);
        var bridge = new StubBridge();

        var track = new TrackEntity
        {
            Title = "Electric Feel",
            Artist = "MGMT",
            YouTubeId = "mgmt_electric",
            ReleaseCatalogId = "single:mgmt_electric",
            ArtistCatalogId = "artist:mgmt"
        };
        store.Upsert(track);
        catalog.RebuildFromTrackMetadata();

        var search = new GlobalSearchService(library, catalog, bridge, debounceMs: 0);

        // Scope = Library
        search.Scope = GlobalSearchScope.Library;
        await search.PerformSearchAsync("Electric");

        Assert.Single(search.TrackResults);
        Assert.Equal("Electric Feel", search.TrackResults[0].Title);
        Assert.Empty(search.YouTubeResults);

        // Scope = YouTube
        search.Scope = GlobalSearchScope.YouTube;
        await search.PerformSearchAsync("Electric");

        Assert.Empty(search.TrackResults);
        Assert.Single(search.YouTubeResults);
        Assert.Contains("Electric", search.YouTubeResults[0].Title);

        // Reset
        search.Reset();
        Assert.Equal("", search.Query);
        Assert.Equal(GlobalSearchScope.All, search.Scope);
        Assert.Empty(search.TrackResults);
        Assert.Empty(search.YouTubeResults);
    }
}
