using Muses.Core.Chrome;
using Muses.Core.Discovery;
using Muses.Core.Preferences;

namespace Muses.Tests;

public class ChromeLayoutTests
{
    [Fact]
    public void Apple_Music_key_color_is_FA586A()
    {
        Assert.Equal("FA586A", AppleMusicTokens.KeyColorHex);
        Assert.Equal(250.0 / 255.0, AppleMusicTokens.KeyColorRgb.R, 4);
        Assert.Equal(88.0 / 255.0, AppleMusicTokens.KeyColorRgb.G, 4);
        Assert.Equal(106.0 / 255.0, AppleMusicTokens.KeyColorRgb.B, 4);
    }

    [Fact]
    public void Dark_page_background_is_measured_AM_Web_1F1F1F()
    {
        Assert.Equal(31.0 / 255.0, AppleMusicTokens.DarkPageRgb.R, 4);
        Assert.Equal(34, AppleMusicTokens.PageTitleSize);
        Assert.Equal(22, AppleMusicTokens.SectionTitleSize);
        Assert.InRange(AppleMusicTokens.SidebarWidth, 232, 260);
        Assert.Equal(12, AppleMusicTokens.CardCorner);
        Assert.Equal(540, AppleMusicTokens.EditorialWidth);
        Assert.Equal(309, AppleMusicTokens.EditorialHeight);
    }

    [Fact]
    public void Library_sidebar_is_permanently_present()
    {
        Assert.True(LibraryChromePolicy.SidebarIsPermanent);
    }

    [Fact]
    public void Selected_chrome_glyph_uses_accent_and_no_glow()
    {
        Assert.Equal(0, ChromeGlyphStyle.SelectedGlowRadius);
        Assert.True(ChromeGlyphStyle.SelectedUsesAccent);
    }

    [Fact]
    public void Search_is_one_independent_floating_glass_window()
    {
        Assert.Equal("Alpha", SearchChromePolicy.TopResult(["Alpha", "Alpine", "Beta"], "alp"));
        Assert.True(SearchChromePolicy.PresentsAsFloatingGlass);
        Assert.Equal(680, SearchChromePolicy.PanelMaxWidth);
        Assert.Equal(18, SearchChromePolicy.PanelCorner);
        Assert.Equal("plus", SearchChromePolicy.AddMusicSystemImage);
        Assert.Equal("search", SearchWindowPolicy.SceneId);
        Assert.True(SearchWindowPolicy.IsSingleInstance);
        Assert.True(SearchWindowPolicy.ClosesOnEscape);
        Assert.Equal(680, SearchWindowPolicy.DefaultWidth);
        Assert.Equal(620, SearchWindowPolicy.DefaultHeight);
        Assert.Equal(600, SearchWindowPolicy.MinimumWidth);
        Assert.Equal(520, SearchWindowPolicy.MinimumHeight);
        Assert.Equal(32, SearchWindowPolicy.ScreenEdgeInset);
        Assert.Equal(52, SearchWindowPolicy.DraggableHeaderHeight);
        Assert.Equal(24, SearchWindowPolicy.ContentInset);
        Assert.Equal(44, SearchWindowPolicy.ControlHeight);
        Assert.Equal(34, SearchWindowPolicy.SourceSegmentHeight);
        Assert.Equal(68, SearchWindowPolicy.ResultRowHeight);
    }

    [Fact]
    public void Dock_lyrics_stays_in_the_dock_while_artwork_owns_Now_Playing_entry()
    {
        Assert.Equal(DockLyricsAction.ToggleDrawer, DockLyricsPolicy.Action(nowPlayingOpen: false));
        Assert.Equal(DockLyricsAction.ToggleLyricsFocus, DockLyricsPolicy.Action(nowPlayingOpen: true));
    }

    [Fact]
    public void Top_Picks_prefers_hero_then_mixed_then_recent_max_three_unique()
    {
        DiscoveryItem Yt(string id) => new DiscoveryItem.YouTube(new YouTubeDiscoveryCard(id, id));
        var picks = TopPicksResolver.Picks(
            hero: Yt("h"),
            mixed: [Yt("h"), Yt("m1"), Yt("m2")],
            recent: [Yt("m1"), Yt("r1")],
            max: 3);
        Assert.Equal(["yt:h", "yt:m1", "yt:m2"], picks.Select(p => p.Id));
    }

    [Fact]
    public void Top_Picks_does_not_fabricate_cards()
    {
        var picks = TopPicksResolver.Picks(
            hero: null,
            mixed: [],
            recent: [new DiscoveryItem.YouTube(new YouTubeDiscoveryCard("only", "only"))],
            max: 3);
        Assert.Single(picks);
    }

    [Fact]
    public void New_featured_slot_is_the_first_available_item()
    {
        DiscoveryItem[] items =
        [
            new DiscoveryItem.YouTube(new YouTubeDiscoveryCard("a", "A")),
            new DiscoveryItem.YouTube(new YouTubeDiscoveryCard("b", "B"))
        ];
        Assert.Equal("yt:a", NewFeaturedResolver.Featured(items)?.Id);
        Assert.Null(NewFeaturedResolver.Featured([]));
    }

    [Fact]
    public void Home_and_New_keep_distinct_Apple_Music_page_templates()
    {
        Assert.True(HomePagePolicy.TopPicksUsePortraitCards);
        Assert.True(HomePagePolicy.AdditionalShelvesUseSquareCards);
        Assert.True(NewPagePolicy.FeaturedUsesLandscapeEditorialCards);
        Assert.True(NewPagePolicy.BestNewSongsUsesAdaptiveMatrix);
        Assert.Equal(280, NewPagePolicy.CompactSongColumnMinimum);
    }

    [Fact]
    public void Settings_occupies_content_while_presented()
    {
        Assert.True(SettingsChromePolicy.ShowsAccount(true));
        Assert.False(SettingsChromePolicy.ShowsAccount(false));
    }

    [Fact]
    public void Apple_Music_Web_shell_is_left_nav_plus_floating_capsule()
    {
        Assert.True(AppleMusicChrome.PrimaryNavInSidebar);
        Assert.True(AppleMusicChrome.PlayerIsFloatingCapsule);
        Assert.True(AppleMusicChrome.SelectedNavUsesAccent);
        Assert.Equal(540, AppleMusicTokens.EditorialWidth);
    }

    [Fact]
    public void Live_AM_Web_spacing_tokens()
    {
        Assert.Equal(8, AppleMusicTokens.SidebarInset);
        Assert.Equal(244, AppleMusicTokens.SidebarWidth);
        Assert.Equal(20, AppleMusicTokens.PlayerBottomMargin);
        Assert.Equal(16, AppleMusicTokens.PlayerHorizontalMargin);
        Assert.Equal(540, AppleMusicTokens.EditorialWidth);
        Assert.Equal(309, AppleMusicTokens.EditorialHeight);
        Assert.Equal(40, AppleMusicTokens.ContentPaddingX);
        Assert.Equal(AppleMusicTokens.ContentPaddingX, AppleMusicSpacing.PageHorizontal);
        Assert.Equal(18, AppleMusicSpacing.PageTop);
        Assert.Equal(32, AppleMusicSpacing.BrowseTitleTop);
        Assert.Equal(28, AppleMusicSpacing.HeaderToPrimary);
        Assert.Equal(20, AppleMusicSpacing.Related);
        Assert.True(AppleMusicSpacing.Section > AppleMusicSpacing.ShelfContent);
        Assert.Equal(34, AppleMusicTokens.NavItemHeight);
        Assert.Equal(668, AppleMusicTokens.CapsuleWidth);
        Assert.True(AppleMusicTokens.CollectionDeckRoomyFooterHeight > 0);
        Assert.True(AppleMusicTokens.CollectionDeckRoomyCardWidth
                    < AppleMusicTokens.CollectionDeckRoomyCardWidth
                    + AppleMusicTokens.CollectionDeckRoomyFooterHeight);
        Assert.True(AppleMusicTokens.CollectionDeckCompactBreakpoint
                    < AppleMusicTokens.CollectionDeckWideBreakpoint);
        Assert.True(AppleMusicTokens.CollectionDeckHandleHeight >= 44);
    }

    [Fact]
    public void Player_capsule_overlays_content_and_does_not_reserve_a_row()
    {
        Assert.True(PlayerLayoutPolicy.IsWindowOverlay);
        Assert.True(PlayerTransportPolicy.LeadingClusterIsTransport);
        Assert.True(PlayerTransportPolicy.IdentityIsCentered);
        Assert.True(PlayerControlPolicy.UsesSingleVolumeEntry);
        Assert.True(PlayerControlPolicy.UsesYouTubeMark);
        Assert.True(PlayerControlPolicy.HidesExpandControl);
        Assert.True(PlayerControlPolicy.NowPlayingOpensFromArtwork);
        Assert.False(LibraryChromePolicy.ShowsInbox);
        var minimumDetailWidth = WindowChromeMetrics.MinimumWidth
            - AppleMusicTokens.SidebarWidth
            - WindowChromeMetrics.SidebarOuterInset;
        Assert.True(AppleMusicTokens.CapsuleWidth > minimumDetailWidth);
        Assert.True(minimumDetailWidth - 2 * AppleMusicTokens.PlayerHorizontalMargin > 0);
        Assert.True(ProductionPlaybackPolicy.IsYouTubeOnly);
    }

    [Fact]
    public void Queue_is_an_integrated_trailing_pane()
    {
        Assert.True(QueueChromePolicy.IsIntegratedTrailingPane);
        Assert.False(QueueChromePolicy.IsDetachedRoundedCard);
        Assert.Equal(360, QueueChromePolicy.Width);
    }

    [Fact]
    public void Now_Playing_covers_the_window_and_hides_the_dock()
    {
        Assert.True(NowPlayingChromePolicy.CoversWindow);
        Assert.True(NowPlayingChromePolicy.HidesDock);
        Assert.True(NowPlayingChromePolicy.CanOpen(hasTrack: true));
        Assert.False(NowPlayingChromePolicy.CanOpen(hasTrack: false));
    }

    [Fact]
    public void Settings_owns_global_keyboard_input_over_retained_Now_Playing()
    {
        Assert.True(NowPlayingInputPolicy.AcceptsGlobalKeyEvents(true, false));
        Assert.False(NowPlayingInputPolicy.AcceptsGlobalKeyEvents(true, true));
        Assert.False(NowPlayingInputPolicy.AcceptsGlobalKeyEvents(false, false));
        Assert.Equal(0.30, NowPlayingPresentationPolicy.DismissDuration);
        Assert.True(NowPlayingPresentationPolicy.AcceptsInteraction(true, false));
        Assert.False(NowPlayingPresentationPolicy.AcceptsInteraction(false, false));
        Assert.False(NowPlayingPresentationPolicy.IsAccessibilityVisible(true, true));
    }

    [Fact]
    public void Windows_caption_is_native_and_on_the_right()
    {
        Assert.True(CaptionPolicy.UsesNativeCaptionButtons);
        Assert.False(CaptionPolicy.ReparentsStandardButtons);
        Assert.False(CaptionPolicy.DrawsFakeTrafficLights);
        Assert.Equal(CaptionSide.Right, CaptionPolicy.PreferredSide);
        Assert.Equal(0, WindowChromeMetrics.CaptionClearanceWidth);
        Assert.Equal(32, WindowChromeMetrics.CaptionDragHeight);
        Assert.Equal(840, WindowChromeMetrics.MinimumWidth);
        Assert.Equal(600, WindowChromeMetrics.MinimumHeight);
    }

    [Fact]
    public void Inbox_is_not_a_chrome_destination()
    {
        Assert.False(LibraryChromePolicy.ShowsInbox);
        Assert.DoesNotContain(SidebarSection.Inbox, ChromeDestinations.Visible);
    }

    [Fact]
    public void Magenta_is_identical_in_light_and_dark()
    {
        Assert.Equal(BrandColors.Magenta.Dark, BrandColors.Magenta.Light);
        Assert.Equal("FA586A", BrandColors.Magenta.Dark.ToHexRgb());
        Assert.Equal("1F1F1F", BrandColors.Background.Dark.ToHexRgb());
        Assert.Equal("FFFFFF", BrandColors.Background.Light.ToHexRgb());
        Assert.Equal("FF0000", BrandColors.YouTubeRed.ToHexRgb());
    }

    [Fact]
    public void Motion_tokens_honor_reduce_motion()
    {
        Assert.Equal(0.15, MusesMotion.Hover);
        Assert.Null(MusesMotion.HoverSeconds(reduceMotion: true));
        Assert.Equal(0.15, MusesMotion.HoverSeconds(reduceMotion: false));
        Assert.Null(MusesMotion.MorphSeconds(reduceMotion: true));
        Assert.Equal(0.32, MusesMotion.MorphSeconds(reduceMotion: false));
    }

    [Fact]
    public void Tray_is_on_by_default_and_intrusive_desktop_flags_are_off()
    {
        Assert.True(FeatureFlagDefaults.EnabledByDefault[PrefKey.FfTray]);
        Assert.False(FeatureFlagDefaults.EnabledByDefault.ContainsKey(PrefKey.FfMiniPlayer));
        Assert.False(FeatureFlagDefaults.EnabledByDefault.ContainsKey(PrefKey.FfDesktopLyrics));
        Assert.False(FeatureFlagDefaults.EnabledByDefault.ContainsKey(PrefKey.FfGlobalHotkeys));
    }

    [Fact]
    public void Spacing_tokens_match_Apple_Music_Web_metrics()
    {
        Assert.Equal(40, AppleMusicSpacing.PageHorizontal);
        Assert.Equal(18, AppleMusicSpacing.PageTop);
        Assert.Equal(32, AppleMusicSpacing.BrowseTitleTop);
        Assert.Equal(28, AppleMusicSpacing.HeaderToPrimary);
        Assert.Equal(34, AppleMusicSpacing.Section);
        Assert.Equal(13, AppleMusicSpacing.ShelfContent);
        Assert.Equal(18, AppleMusicSpacing.ShelfItem);
        Assert.Equal(96, OverlayChromeMetrics.ScrollBottomInset);
    }

    [Fact]
    public void Player_bar_and_drawer_metrics_match_macOS_Muses_specs()
    {
        Assert.Equal(668, PlayerDockMetrics.CapsuleWidth);
        Assert.Equal(56, PlayerDockMetrics.Height);
        Assert.Equal(40, PlayerDockMetrics.Art);
        Assert.Equal(32, PlayerDockMetrics.Play);
        Assert.Equal(28, PlayerDockMetrics.ProgressHorizontalInset);
        Assert.True(QueueChromePolicy.IsIntegratedTrailingPane);
        Assert.Equal(360, QueueChromePolicy.Width);
    }

    [Fact]
    public void Playlist_overview_geometry_matches_spec()
    {
        Assert.Equal(260, PlaylistOverviewMetrics.CardWidth);
        Assert.Equal(330, PlaylistOverviewMetrics.CardHeight);
        Assert.Equal(20, PlaylistOverviewMetrics.CornerRadius);
        Assert.Equal(24, PlaylistOverviewMetrics.ColumnSpacing);
        Assert.Equal(30, PlaylistOverviewMetrics.RowSpacing);
    }
}

public static class ChromeDestinations
{
    public static readonly SidebarSection[] Visible =
    [
        SidebarSection.Search,
        SidebarSection.Home,
        SidebarSection.Discover,
        SidebarSection.Recently,
        SidebarSection.Songs,
        SidebarSection.Albums,
        SidebarSection.Artists,
        SidebarSection.History,
        SidebarSection.Playlists
    ];
}
