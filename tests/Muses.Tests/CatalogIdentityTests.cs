using Muses.Core.Catalog;
using Muses.Core.Library;
using Muses.Infrastructure.Catalog;
using Muses.Persistence;

namespace Muses.Tests;

public class CatalogIdentityTests
{
    [Fact]
    public void Releases_with_distinct_stable_ids_never_merge_even_if_names_match()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var catalog = new YouTubeCatalogService(store, store);

        // Two different releases by same name but different stable IDs (e.g. self-titled album vs single)
        var t1 = new TrackEntity
        {
            Title = "Genesis",
            Artist = "Dua Lipa",
            AlbumTitle = "Dua Lipa",
            YouTubeId = "vid1",
            ReleaseCatalogId = "album:dua lipa:dua lipa",
            ArtistCatalogId = "artist:dua lipa"
        };
        var t2 = new TrackEntity
        {
            Title = "Dua Lipa",
            Artist = "Dua Lipa",
            YouTubeId = "vid2",
            ReleaseCatalogId = "single:vid2",
            ArtistCatalogId = "artist:dua lipa"
        };

        store.Upsert(t1);
        store.Upsert(t2);

        catalog.RebuildFromTrackMetadata();
        var releases = catalog.Releases();

        Assert.Equal(2, releases.Count);
        Assert.Contains(releases, r => r.StableId == "album:dua lipa:dua lipa" && r.Kind == CatalogReleaseKind.Album);
        Assert.Contains(releases, r => r.StableId == "single:vid2" && r.Kind == CatalogReleaseKind.Single);
    }

    [Fact]
    public void Artists_with_distinct_channel_ids_never_merge_even_if_display_names_match()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var catalog = new YouTubeCatalogService(store, store);

        // Two artists with same display name "John Smith" but different channel IDs
        var t1 = new TrackEntity
        {
            Title = "Song A",
            Artist = "John Smith",
            YouTubeId = "vid_a",
            ReleaseCatalogId = "single:vid_a",
            ArtistCatalogId = "channel:UC1111111111111111111111"
        };
        var t2 = new TrackEntity
        {
            Title = "Song B",
            Artist = "John Smith",
            YouTubeId = "vid_b",
            ReleaseCatalogId = "single:vid_b",
            ArtistCatalogId = "channel:UC2222222222222222222222"
        };

        store.Upsert(t1);
        store.Upsert(t2);

        catalog.RebuildFromTrackMetadata();
        var artists = catalog.Artists();

        Assert.Equal(2, artists.Count);
        Assert.Contains(artists, a => a.StableId == "channel:UC1111111111111111111111");
        Assert.Contains(artists, a => a.StableId == "channel:UC2222222222222222222222");
    }

    [Theory]
    [InlineData("OLAK5uy_k1234567890", true)]
    [InlineData("PL1234567890abcdef", false)]
    [InlineData("RD1234567890", false)]
    [InlineData("", false)]
    public void IsMusicAlbum_identifies_olak_playlists_only(string playlistId, bool expected)
    {
        Assert.Equal(expected, YouTubePlaylistID.IsMusicAlbum(playlistId));
    }

    [Fact]
    public void Stable_identity_generators_follow_spec()
    {
        Assert.Equal("browse:MPREb_123", YouTubeCatalogIdentity.Release("MPREb_123", null));
        Assert.Equal("playlist:OLAK5uy_abc", YouTubeCatalogIdentity.Release(null, "OLAK5uy_abc"));
        Assert.Equal("channel:UCxyz", YouTubeCatalogIdentity.Artist("UCxyz", null));
        Assert.Equal("browse:UCxyz", YouTubeCatalogIdentity.Artist(null, "UCxyz"));
    }
}
