using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Persistence;

namespace Muses.Tests;

public class PlaylistServiceTests
{
    private static (SqliteStore store, PlaylistService service) MakeService()
    {
        var store = SqliteStore.Open(inMemory: true);
        var service = new PlaylistService(store);
        return (store, service);
    }

    private static TrackEntity MakeTrack(SqliteStore store, string title = "Test Track")
    {
        var track = new TrackEntity
        {
            Id = Guid.NewGuid(),
            Title = title,
            Artist = "Artist",
            DurationMs = 200000,
            YouTubeId = "test-video"
        };
        store.Upsert(track);
        return track;
    }

    [Fact]
    public void Create_creates_empty_playlist()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var playlist = service.Create("My Playlist");
            Assert.Equal("My Playlist", playlist.Name);
            Assert.Empty(playlist.Items);

            var playlists = service.FetchAll();
            Assert.Single(playlists);
            Assert.Equal("My Playlist", playlists[0].Name);
        }
    }

    [Fact]
    public void AddTrack_appends_track_and_deduplicates()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var track = MakeTrack(store, "Song A");
            var playlist = service.Create("P1");
            service.AddTrack(playlist, track);

            var p = service.Get(playlist.Id);
            Assert.NotNull(p);
            Assert.Single(p!.Items);
            Assert.Equal(0, p.Items[0].ItemOrder);
            Assert.Equal("Song A", p.Items[0].Track?.Title);

            // Adding the same track again -> deduplicated
            service.AddTrack(playlist, track);
            var p2 = service.Get(playlist.Id);
            Assert.NotNull(p2);
            Assert.Single(p2!.Items);
        }
    }

    [Fact]
    public void MoveItem_reorders_items()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var t1 = MakeTrack(store, "A");
            var t2 = MakeTrack(store, "B");
            var t3 = MakeTrack(store, "C");

            var playlist = service.Create("P2");
            service.AddTrack(playlist, t1);
            service.AddTrack(playlist, t2);
            service.AddTrack(playlist, t3);

            var p = service.Get(playlist.Id);
            Assert.NotNull(p);
            Assert.Equal(new[] { "A", "B", "C" }, p!.Items.Select(i => i.Track?.Title));

            // Move C (index 2) to index 0
            service.MoveItem(p, 2, 0);

            var pAfter = service.Get(playlist.Id);
            Assert.NotNull(pAfter);
            Assert.Equal(new[] { "C", "A", "B" }, pAfter!.Items.Select(i => i.Track?.Title));
            Assert.Equal(new[] { 0, 1, 2 }, pAfter.Items.Select(i => i.ItemOrder));
        }
    }

    [Fact]
    public void Delete_cascades_playlist_items()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var track = MakeTrack(store, "Doomed Song");
            var playlist = service.Create("To Delete");
            service.AddTrack(playlist, track);

            var p = service.Get(playlist.Id);
            Assert.NotNull(p);
            Assert.Single(p!.Items);

            service.Delete(playlist);

            Assert.Null(service.Get(playlist.Id));
            Assert.Empty(service.FetchAll());
            // Track survives in store
            var savedTrack = store.Get(track.Id);
            Assert.NotNull(savedTrack);
        }
    }

    [Fact]
    public void DeleteWithUndoSnapshot_restores_order_and_pinned_state()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var a = MakeTrack(store, "A");
            var b = MakeTrack(store, "B");
            var playlist = service.Create("Recover me");
            service.AddTrack(playlist, a);
            service.AddTrack(playlist, b);
            service.TogglePin(playlist);

            var snapshot = service.DeleteWithUndoSnapshot(playlist);
            Assert.NotNull(snapshot);
            Assert.Empty(service.FetchAll());

            var restored = service.Restore(snapshot!);
            Assert.NotNull(restored);
            Assert.Equal("Recover me", restored!.Name);
            Assert.True(restored.Pinned);
            Assert.Equal(new[] { "A", "B" }, restored.Items.Select(i => i.Track?.Title));
        }
    }

    [Fact]
    public void TogglePin_toggles_pinned_state()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var playlist = service.Create("Pinned List");
            Assert.False(playlist.Pinned);

            service.TogglePin(playlist);
            Assert.True(playlist.Pinned);
            Assert.Single(service.PinnedPlaylists());

            service.TogglePin(playlist);
            Assert.False(playlist.Pinned);
            Assert.Empty(service.PinnedPlaylists());

            // Test Guid overload
            service.TogglePin(playlist.Id);
            Assert.Single(service.PinnedPlaylists());
        }
    }

    [Fact]
    public void Guid_overloads_for_remove_and_delete_with_undo()
    {
        var (store, service) = MakeService();
        using (store)
        {
            var a = MakeTrack(store, "A");
            var pl = service.Create("Test PL");
            service.AddTrack(pl, a);

            var loaded = service.Get(pl.Id);
            Assert.NotNull(loaded);
            var item = loaded!.Items[0];

            service.RemoveItem(item.Id);
            loaded = service.Get(pl.Id);
            Assert.Empty(loaded!.Items);

            var snapshot = service.DeleteWithUndoSnapshot(pl.Id);
            Assert.NotNull(snapshot);
            Assert.Empty(service.FetchAll());
        }
    }
}
