using Muses.Core.Chrome;
using Muses.Core.Domain;
using Muses.Core.Library;

namespace Muses.Tests;

public class CollectionPresentationTests
{
    [Fact]
    public void Collection_header_rhythm()
    {
        Assert.Equal(40, AppleMusicSpacing.PageHorizontal);
        Assert.Equal(32, AppleMusicSpacing.BrowseTitleTop);
        Assert.Equal(28, AppleMusicSpacing.HeaderToPrimary);
        Assert.Equal(20, AppleMusicSpacing.Related);
        Assert.Equal(34, AppleMusicSpacing.Section);
        Assert.Equal(34, AppleMusicTokens.PageTitleSize);
    }

    [Fact]
    public void Songs_canonical_order_is_title_az_and_deterministic()
    {
        var tracks = new[]
        {
            new TrackEntity { Title = "Zulu", Artist = "B", YouTubeId = "z" },
            new TrackEntity { Title = "Alpha", Artist = "Z", YouTubeId = "a2" },
            new TrackEntity { Title = "Alpha", Artist = "A", YouTubeId = "a1" },
            new TrackEntity { Title = "Delta", Artist = "C", YouTubeId = "d1" }
        };

        var rows = CollectionTrackRow.Songs(tracks);

        Assert.Equal(new[] { "Alpha", "Alpha", "Delta", "Zulu" }, rows.Select(r => r.Title));
        Assert.Equal(new[] { "A", "Z", "C", "B" }, rows.Select(r => r.Artist));
        Assert.Equal(new[] { 0, 1, 2, 3 }, rows.Select(r => r.CanonicalIndex));
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Snapshot.YouTubeId)));
    }

    [Fact]
    public void Legacy_video_backed_tracks_remain_ordinary_songs_rows()
    {
        var video = new TrackEntity
        {
            Id = Guid.NewGuid(),
            Title = "Wide Video",
            Artist = "Artist",
            YouTubeId = "video-id",
            MediaKindRaw = "musicVideo"
        };

        var rows = CollectionTrackRow.Songs([video]);

        Assert.Equal(new[] { video.Id }, rows.Select(r => r.Id));
        Assert.Equal("musicVideo", video.MediaKindRaw);
    }

    [Fact]
    public void Playlist_rows_use_persisted_playlist_item_order()
    {
        var first = new TrackEntity { Id = Guid.NewGuid(), Title = "First", Artist = "A", YouTubeId = "first" };
        var second = new TrackEntity { Id = Guid.NewGuid(), Title = "Second", Artist = "B", YouTubeId = "second" };
        var item0 = new PlaylistItemEntity { Id = Guid.NewGuid(), ItemOrder = 0, Track = first, TrackId = first.Id };
        var item1 = new PlaylistItemEntity { Id = Guid.NewGuid(), ItemOrder = 1, Track = second, TrackId = second.Id };

        var rows = CollectionTrackRow.Playlist([item1, item0]);

        Assert.Equal(new[] { "First", "Second" }, rows.Select(r => r.Title));
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.CanonicalIndex));
    }

    [Fact]
    public void YouTube_import_rows_use_persisted_order_and_fallback_snapshot()
    {
        var track = new TrackEntity { Id = Guid.NewGuid(), Title = "Resolved Track", Artist = "Artist X", YouTubeId = "yt-resolved" };
        var item0 = new YouTubeImportItemEntity
        {
            Id = Guid.NewGuid(),
            ItemOrder = 0,
            YouTubeId = "yt-resolved",
            Title = "Resolved Track",
            Track = track,
            TrackId = track.Id
        };
        var item1 = new YouTubeImportItemEntity
        {
            Id = Guid.NewGuid(),
            ItemOrder = 1,
            YouTubeId = "yt-unresolved",
            Title = "Raw Title",
            Track = null,
            TrackId = null
        };

        var rows = CollectionTrackRow.YouTubeImport([item1, item0]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Resolved Track", rows[0].Title);
        Assert.Equal(0, rows[0].CanonicalIndex);
        Assert.Equal("Raw Title", rows[1].Title);
        Assert.Equal(1, rows[1].CanonicalIndex);
        Assert.Equal("yt-unresolved", rows[1].Snapshot.YouTubeId);
    }

    [Fact]
    public void Visual_table_sorting_does_not_rewrite_canonical_order()
    {
        var rows = new[]
        {
            MakeRow("B", "Zulu", 0),
            MakeRow("A", "Alpha", 1)
        };

        var visuallySorted = CollectionTrackSort.Rows(rows, CollectionColumn.Artist, ascending: true);

        Assert.Equal(new[] { "A", "B" }, visuallySorted.Select(r => r.Title));
        Assert.Equal(new[] { "B", "A" }, rows.Select(r => r.Title));
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.CanonicalIndex));
    }

    [Fact]
    public void Default_sorts_distinguish_songs_from_playlists()
    {
        var rows = new[]
        {
            MakeRow("B", "A", 0),
            MakeRow("A", "B", 1)
        };

        Assert.Equal(new[] { "A", "B" },
            CollectionTrackSort.Rows(rows, CollectionTableDefaultSort.TitleAZ).Select(r => r.Title));
        Assert.Equal(new[] { "B", "A" },
            CollectionTrackSort.Rows(rows, CollectionTableDefaultSort.PlaylistOrder).Select(r => r.Title));
    }

    [Fact]
    public void Responsive_deck_geometry()
    {
        var roomy = CollectionDeckGeometry.Resolve(containerWidth: 980, containerHeight: 760);
        var compact = CollectionDeckGeometry.Resolve(containerWidth: 560, containerHeight: 760);
        var compactHeight = CollectionDeckGeometry.Resolve(containerWidth: 980, containerHeight: 600);

        Assert.Equal(4, roomy.Radius);
        Assert.Equal(2, compact.Radius);
        Assert.Equal(2, compactHeight.Radius);
        Assert.Equal(9, CollectionDeckProjection.VisibleIndices(count: 100, position: 50, radius: roomy.Radius).Count);
        Assert.Equal(5, CollectionDeckProjection.VisibleIndices(count: 100, position: 50, radius: compact.Radius).Count);
        Assert.True(roomy.CardHeight > roomy.CardWidth);
        Assert.Equal(118, roomy.LowerFanClearance);
        Assert.Equal(66, compact.LowerFanClearance);
        Assert.True(roomy.ViewportHeight > roomy.CardHeight + CollectionDeckScrubberMetrics.ThumbHeight);
    }

    [Fact]
    public void Deck_projection_boundaries()
    {
        Assert.Empty(CollectionDeckProjection.VisibleIndices(count: 0, position: 0, radius: 4));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 },
            CollectionDeckProjection.VisibleIndices(count: 20, position: -8, radius: 4));
        Assert.Equal(new[] { 15, 16, 17, 18, 19 },
            CollectionDeckProjection.VisibleIndices(count: 20, position: 99, radius: 4));
    }

    [Fact]
    public void Deck_scrubber_geometry()
    {
        Assert.Equal(460, CollectionDeckScrubberMetrics.Width(availableWidth: 980));
        Assert.Equal(328, CollectionDeckScrubberMetrics.Width(availableWidth: 548));
        Assert.Equal(4, CollectionDeckScrubberMetrics.TrackHeight);
        Assert.Equal(46, CollectionDeckScrubberMetrics.ThumbWidth);
        Assert.Equal(24, CollectionDeckScrubberMetrics.ThumbHeight);
        Assert.True(CollectionDeckScrubberMetrics.StageClearance >= CollectionDeckScrubberMetrics.ThumbHeight);
        Assert.True(CollectionDeckScrubberMetrics.PlayerClearance > OverlayChromeMetrics.ScrollBottomInset);

        const double width = 500;
        Assert.Equal(23, CollectionDeckScrubberMetrics.ThumbCenterX(position: 0, itemCount: 20, width: width));
        Assert.Equal(477, CollectionDeckScrubberMetrics.ThumbCenterX(position: 19, itemCount: 20, width: width));
        Assert.Equal(23, CollectionDeckScrubberMetrics.ThumbCenterX(position: -8, itemCount: 20, width: width));
        Assert.Equal(477, CollectionDeckScrubberMetrics.ThumbCenterX(position: 99, itemCount: 20, width: width));
        Assert.Equal(0, CollectionDeckScrubberMetrics.Position(locationX: 23, itemCount: 20, width: width));
        Assert.Equal(19, CollectionDeckScrubberMetrics.Position(locationX: 477, itemCount: 20, width: width));
        Assert.Equal(0, CollectionDeckScrubberMetrics.Position(locationX: -40, itemCount: 20, width: width));
        Assert.Equal(19, CollectionDeckScrubberMetrics.Position(locationX: 540, itemCount: 20, width: width));
        Assert.Equal(0, CollectionDeckScrubberMetrics.Position(locationX: 250, itemCount: 1, width: width));
    }

    [Fact]
    public void Deck_activation_semantics()
    {
        Assert.True(CollectionDeckActivationPolicy.IsPrimaryActivation(CollectionDeckActivationSource.Pointer));
        Assert.True(CollectionDeckActivationPolicy.IsPrimaryActivation(CollectionDeckActivationSource.Keyboard));
        Assert.True(CollectionDeckActivationPolicy.IsPrimaryActivation(CollectionDeckActivationSource.Accessibility));
        Assert.False(CollectionDeckActivationPolicy.IsPrimaryActivation(CollectionDeckActivationSource.ContextMenu));
    }

    [Fact]
    public void Ambient_artwork_presentation()
    {
        Assert.NotEqual(ArtworkPresentation.FitOnAmbient, ArtworkPresentation.Fill);
    }

    [Fact]
    public void Deck_event_monitor_honors_inherited_enabled_state()
    {
        Assert.True(CollectionDeckInputPolicy.AcceptsEvents(stageEnabled: true, environmentEnabled: true));
        Assert.False(CollectionDeckInputPolicy.AcceptsEvents(stageEnabled: true, environmentEnabled: false));
        Assert.False(CollectionDeckInputPolicy.AcceptsEvents(stageEnabled: false, environmentEnabled: true));
    }

    [Fact]
    public void Deck_projected_snap()
    {
        var forward = CollectionDeckProjection.ProjectedIndex(
            startPosition: 10,
            translation: -86,
            predictedTranslation: -1000,
            spread: 86,
            count: 20);
        var beforeFirst = CollectionDeckProjection.ProjectedIndex(
            startPosition: 1,
            translation: 500,
            predictedTranslation: 900,
            spread: 86,
            count: 20);
        var afterLast = CollectionDeckProjection.ProjectedIndex(
            startPosition: 18,
            translation: -500,
            predictedTranslation: -900,
            spread: 86,
            count: 20);

        Assert.Equal(17, forward);
        Assert.Equal(0, beforeFirst);
        Assert.Equal(19, afterLast);
    }

    [Fact]
    public void Expansion_gesture_direction()
    {
        Assert.True(CollectionDeckProjection.AcceptsVerticalGesture(
            translationWidth: 8, translationHeight: -52, direction: CollectionExpansionDirection.Up));
        Assert.True(CollectionDeckProjection.AcceptsVerticalGesture(
            translationWidth: 4, translationHeight: 56, direction: CollectionExpansionDirection.Down));
        Assert.False(CollectionDeckProjection.AcceptsVerticalGesture(
            translationWidth: 70, translationHeight: -55, direction: CollectionExpansionDirection.Up));
        Assert.False(CollectionDeckProjection.AcceptsVerticalGesture(
            translationWidth: 4, translationHeight: 40, direction: CollectionExpansionDirection.Down));
        Assert.False(CollectionDeckProjection.AcceptsVerticalGesture(
            translationWidth: 4, translationHeight: 56, direction: CollectionExpansionDirection.Up));
    }

    [Fact]
    public void Collection_motion_policy()
    {
        Assert.True(MusesMotion.CollectionDeckSnap >= 0.12);
        Assert.True(MusesMotion.CollectionDeckSnap <= 0.24);
        Assert.True(MusesMotion.CollectionListTransition >= 0.26);
        Assert.True(MusesMotion.CollectionListTransition <= 0.32);
        Assert.Equal(0.30, MusesMotion.CollectionCardActivation);
        Assert.Null(MusesMotion.CollectionDeckSeconds(reduceMotion: true));
        Assert.Null(MusesMotion.CollectionListSeconds(reduceMotion: true));
        Assert.Null(MusesMotion.CollectionCardSeconds(reduceMotion: true));
        Assert.NotNull(MusesMotion.CollectionDeckSeconds(reduceMotion: false));
        Assert.NotNull(MusesMotion.CollectionListSeconds(reduceMotion: false));
        Assert.NotNull(MusesMotion.CollectionCardSeconds(reduceMotion: false));
    }

    private static CollectionTrackRow MakeRow(string title, string artist, int canonicalIndex)
    {
        return new CollectionTrackRow(
            new TrackSnapshot(
                Guid.NewGuid(),
                title,
                artist,
                null,
                120,
                Guid.NewGuid().ToString(),
                null),
            canonicalIndex);
    }
}
