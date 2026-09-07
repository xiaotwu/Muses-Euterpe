using Muses.Core.Library;
using Muses.Persistence;

namespace Muses.Tests;

public class YouTubeImportTests
{
    private sealed class FakeFetcher : IYouTubePlaylistFetcher
    {
        public List<YouTubePlaylistEntry> Entries { get; set; } = [];

        public Task<IReadOnlyList<YouTubePlaylistEntry>> FetchPlaylistAsync(string url, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<YouTubePlaylistEntry>>(Entries);
        }
    }

    [Fact]
    public async Task ImportPlaylist_creates_import_items_and_tracks()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var fetcher = new FakeFetcher
        {
            Entries =
            [
                new YouTubePlaylistEntry("v1", "Track 1", "Artist A", 180, "Awesome Playlist"),
                new YouTubePlaylistEntry("v2", "Track 2", "Artist B", 210, "Awesome Playlist")
            ]
        };
        var service = new YouTubeImportService(store, store, fetcher);

        var importId = await service.ImportPlaylistAsync("https://www.youtube.com/playlist?list=PLtest123");

        var imp = service.GetImport(importId);
        Assert.NotNull(imp);
        Assert.Equal("Awesome Playlist", imp!.Title);
        Assert.Equal("PLtest123", imp.PlaylistId);
        Assert.Equal(2, imp.Items.Count);

        // Verify tracks exist in library
        var track1 = store.GetByYouTubeId("v1");
        var track2 = store.GetByYouTubeId("v2");
        Assert.NotNull(track1);
        Assert.NotNull(track2);
        Assert.Equal("Track 1", track1!.Title);
        Assert.Equal("Track 2", track2!.Title);
    }

    [Fact]
    public async Task Deleting_import_does_not_delete_tracks()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var fetcher = new FakeFetcher
        {
            Entries = [new YouTubePlaylistEntry("v3", "Track 3", "Artist C", 150, "List")]
        };
        var service = new YouTubeImportService(store, store, fetcher);
        var importId = await service.ImportPlaylistAsync("https://www.youtube.com/playlist?list=PLkeepTracks");

        service.HardDelete(importId, deleteAssociatedTracks: false);

        Assert.Null(service.GetImport(importId));
        // Track must still exist in library!
        var track = store.GetByYouTubeId("v3");
        Assert.NotNull(track);
        Assert.Equal("Track 3", track!.Title);
    }

    [Fact]
    public async Task MoveToRecentlyDeleted_and_restore()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var fetcher = new FakeFetcher
        {
            Entries = [new YouTubePlaylistEntry("v4", "Track 4", "Artist D", 190, "List")]
        };
        var service = new YouTubeImportService(store, store, fetcher);
        var importId = await service.ImportPlaylistAsync("https://www.youtube.com/playlist?list=PLdelRec");

        Assert.Single(service.GetActiveImports());
        Assert.Empty(service.GetRecentlyDeletedImports());

        service.MoveToRecentlyDeleted(importId);

        Assert.Empty(service.GetActiveImports());
        Assert.Single(service.GetRecentlyDeletedImports());

        service.Restore(importId);

        Assert.Single(service.GetActiveImports());
        Assert.Empty(service.GetRecentlyDeletedImports());
    }
}
