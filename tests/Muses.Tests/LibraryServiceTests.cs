using Muses.Core.Library;
using Muses.Infrastructure.Catalog;
using Muses.Infrastructure.YTDlp;
using Muses.Persistence;

namespace Muses.Tests;

public class LibraryServiceTests
{
    [Fact]
    public void Playing_discovery_media_keeps_history_without_adding_it_to_collections()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var catalog = new YouTubeCatalogService(store, store);

        var played = library.UpsertFromYouTube("abcdefghijk", "Discovery song", "Artist", 120, null);
        library.RecordPlay(played.Id);

        Assert.Equal(played.Id, library.TrackByYouTubeId(played.YouTubeId)?.Id);
        Assert.Equal(played.Id, Assert.Single(library.RecentlyPlayedTracks(10)).Id);
        Assert.Empty(library.AllTracks());
        Assert.Empty(library.SearchTracks("Discovery"));
        Assert.Empty(catalog.Releases());
        Assert.Empty(catalog.Artists());

        const string channelId = "UC1111111111111111111111";
        var saved = catalog.ImportOnlineTrack(new YTDlpPlaylistEntry(played.YouTubeId, played.Title, played.Artist,
            ChannelId: channelId));

        Assert.Equal(played.Id, saved.Id);
        Assert.Equal(1, Assert.Single(library.AllTracks()).PlayCount);
        Assert.Equal(played.Id, Assert.Single(library.SearchTracks("Discovery")).Id);
        Assert.Single(catalog.Releases());
        Assert.Equal($"channel:{channelId}", Assert.Single(catalog.Artists()).StableId);
    }

    [Fact]
    public void Resolving_existing_media_preserves_library_metadata_and_membership()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var existing = new TrackEntity
        {
            YouTubeId = "abcdefghijk",
            Title = "Edited title",
            Artist = "Edited artist",
            ArtworkUrl = "https://example.test/cover.png",
            DurationMs = 140_000,
            Liked = true
        };
        store.Upsert(existing);
        library.RecordPlay(existing.Id);

        var resolved = library.UpsertFromYouTube(existing.YouTubeId, "Remote title", "Uploader", 100, null);

        Assert.Equal(existing.ToSnapshot(), resolved);
        var saved = Assert.Single(library.AllTracks());
        Assert.Equal(1, saved.PlayCount);
        Assert.True(saved.Liked);
    }

    [Fact]
    public void Saving_a_like_or_local_playlist_item_promotes_playback_only_media()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var playlists = new PlaylistService(store);
        var favorite = library.UpsertFromYouTube("abcdefghijk", "Favorite", "Artist", 120, null);
        var playlistTrack = library.UpsertFromYouTube("abcdefghijl", "Playlist song", "Artist", 120, null);
        var playlist = playlists.Create("Listening");

        library.ToggleLike(favorite.Id);
        library.ToggleLike(favorite.Id);
        playlists.AddTrack(playlist, library.Track(playlistTrack.Id)!);

        Assert.Equal(2, library.AllTracks().Count);
        Assert.Empty(library.LikedTracks());
        Assert.Equal(playlistTrack.Id, Assert.Single(playlists.Get(playlist.Id)!.Items).TrackId);
    }
}
