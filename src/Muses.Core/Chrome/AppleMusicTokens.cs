namespace Muses.Core.Chrome;

/// <summary>
/// Semantic spacing roles. Measured page geometry remains separate from
/// responsive breakpoints so constrained layouts can adapt without drifting.
/// </summary>
public static class AppleMusicSpacing
{
    public const double PageHorizontal = 40;
    public const double PageTop = 18;
    public const double BrowseTitleTop = 32;
    public const double HeaderToPrimary = 28;
    public const double Related = 20;
    public const double Section = 34;
    public const double ShelfContent = 13;
    public const double ShelfItem = 18;
    public const double GridColumn = 20;
    public const double GridRow = 24;
    public const double ChromeOuter = 8;
    public const double ChromeInner = 12;
    public const double TableCell = 9;
}

/// <summary>
/// Measured 2026-08-20 from live music.apple.com CSS (--keyColor, page, type).
/// </summary>
public static class AppleMusicTokens
{
    public const string KeyColorHex = "FA586A";
    public static readonly (double R, double G, double B) KeyColorRgb =
        (250.0 / 255.0, 88.0 / 255.0, 106.0 / 255.0);
    public static readonly (double R, double G, double B) DarkPageRgb =
        (31.0 / 255.0, 31.0 / 255.0, 31.0 / 255.0);
    public static readonly (double R, double G, double B) LightPageRgb = (1.0, 1.0, 1.0);

    public const double PageTitleSize = 34;
    public const double SectionTitleSize = 22;
    public const double SidebarWidth = 244;
    public const double SidebarCollapsedWidth = 88;
    public const double SidebarCorner = 20;
    public const double SidebarInset = AppleMusicSpacing.ChromeOuter;
    public const double CardCorner = 12;
    public const double EditorialWidth = 540;
    public const double EditorialHeight = 309;
    public const double EditorialAspect = EditorialWidth / EditorialHeight;
    public const double ContentPaddingX = AppleMusicSpacing.PageHorizontal;
    public const double MaxContentWidth = 1560;
    public const double ScrollBottomInset = OverlayChromeMetrics.ScrollBottomInset;
    public const double NavItemHeight = 34;
    public const double PlayerBottomMargin = 20;
    public const double PlayerHorizontalMargin = 16;
    public const double CapsuleWidth = 668;
    public const double CapsuleHeight = 56;
    public const double CapsuleCorner = 1000;
    public const double CollectionDeckRoomyCardWidth = 220;
    public const double CollectionDeckCompactCardWidth = 175;
    public const double CollectionDeckRoomyFooterHeight = 68;
    public const double CollectionDeckCompactFooterHeight = 56;
    public const double CollectionDeckRoomySpread = 110;
    public const double CollectionDeckMediumSpread = 96;
    public const double CollectionDeckCompactSpread = 84;
    public const double CollectionDeckWideBreakpoint = 810;
    public const double CollectionDeckCompactBreakpoint = 620;
    public const double CollectionDeckCompactHeight = 680;
    public const double CollectionDeckHoverLift = 12;
    public const double CollectionDeckExpansionThreshold = 48;
    public const double CollectionDeckScrubberHeight = 52;
    public const double CollectionDeckHandleHeight = 44;
    public const double TrackArtworkSize = 38;
}

public static class OverlayChromeMetrics
{
    public const double ScrollBottomInset = 96;
}

/// <summary>Live music.apple.com chrome, not a Sidra top-bar shell.</summary>
public static class AppleMusicChrome
{
    public const bool PrimaryNavInSidebar = true;
    public const bool PlayerIsFloatingCapsule = true;
    public const double EditorialAspect = AppleMusicTokens.EditorialAspect;
    public const bool SelectedNavUsesAccent = true;
}

public static class LibraryChromePolicy
{
    public const bool SidebarIsPermanent = true;
    public const double CollapsedWidth = AppleMusicTokens.SidebarCollapsedWidth;

    /// <summary>Music Inbox tables remain on disk but have no chrome entry.</summary>
    public const bool ShowsInbox = false;
}

public static class ChromeGlyphStyle
{
    public const double SelectedGlowRadius = 0;
    public const bool SelectedUsesAccent = true;
}

public static class SearchChromePolicy
{
    public const bool PresentsAsFloatingGlass = true;
    public const double PanelMaxWidth = 680;
    public const double PanelCorner = 18;
    public const string AddMusicSystemImage = "plus";

    public static string? TopResult(IReadOnlyList<string> titles, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return null;
        foreach (var title in titles)
        {
            if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                return title;
        }
        return titles.Count > 0 ? titles[0] : null;
    }
}

public static class SearchWindowPolicy
{
    public const string SceneId = "search";
    public const double DefaultWidth = 680;
    public const double DefaultHeight = 620;
    public const double MinimumWidth = 600;
    public const double MinimumHeight = 520;
    public const double ScreenEdgeInset = 32;
    public const double DraggableHeaderHeight = 52;
    public const double ContentInset = 24;
    public const double ControlHeight = 44;
    public const double SourceSegmentHeight = 34;
    public const double ResultRowHeight = 68;
    public const bool IsSingleInstance = true;
    public const bool ClosesOnEscape = true;
}

public static class PlaylistOverviewMetrics
{
    public const double MinimumColumnWidth = 250;
    public const double MaximumColumnWidth = 280;
    public const double CardWidth = 260;
    public const double ArtworkHeight = 230;
    public const double FooterHeight = 100;
    public const double CardHeight = ArtworkHeight + FooterHeight;
    public const double CornerRadius = 20;
    public const double ColumnSpacing = 24;
    public const double RowSpacing = 30;
    public const double HoverLift = 5;
    public const double PressedScale = 0.992;
}

public enum DockLyricsAction
{
    ToggleDrawer,
    ToggleLyricsFocus
}

public static class DockLyricsPolicy
{
    public static DockLyricsAction Action(bool nowPlayingOpen) =>
        nowPlayingOpen ? DockLyricsAction.ToggleLyricsFocus : DockLyricsAction.ToggleDrawer;
}

public static class HomePagePolicy
{
    public const bool TopPicksUsePortraitCards = true;
    public const bool AdditionalShelvesUseSquareCards = true;
}

public static class NewPagePolicy
{
    public const bool FeaturedUsesLandscapeEditorialCards = true;
    public const bool BestNewSongsUsesAdaptiveMatrix = true;
    public const double CompactSongColumnMinimum = 280;
}

public static class SettingsChromePolicy
{
    public static bool ShowsAccount(bool isPresented) => isPresented;
    public static bool AllowsUnderlyingInteraction(bool isPresented) => !isPresented;
    public static bool AllowsBrowseInteraction(bool isPresented) =>
        AllowsUnderlyingInteraction(isPresented);

    public const bool DismissesTransientOverlaysOnPresentation = true;
    public const bool PresentsAsFloatingGlass = true;
    public const bool StickyTitle = true;
    public const bool UsesLiquidGlass = true;
    public const double PanelWidth = 520;
    public const double PanelHeight = 560;
    public const double PanelCorner = 18;
}

public static class PlayerLayoutPolicy
{
    public const bool IsWindowOverlay = true;
}

public static class ProductionPlaybackPolicy
{
    public const bool IsYouTubeOnly = true;
}

public static class PlayerTransportPolicy
{
    public const bool LeadingClusterIsTransport = true;
    public const bool IdentityIsCentered = true;
}

public static class PlayerControlPolicy
{
    public const bool UsesSingleVolumeEntry = true;
    public const bool UsesYouTubeMark = true;
    public const bool HidesExpandControl = true;
    public const bool NowPlayingOpensFromArtwork = true;
}

public static class QueueChromePolicy
{
    public const bool IsIntegratedTrailingPane = true;
    public const bool IsDetachedRoundedCard = false;
    public const double Width = 360;
}

public static class NowPlayingChromePolicy
{
    public const bool CoversWindow = true;
    public const bool HidesDock = true;
    public static bool CanOpen(bool hasTrack) => hasTrack;
}

public static class NowPlayingInputPolicy
{
    public static bool AcceptsGlobalKeyEvents(bool nowPlayingPresented, bool settingsPresented) =>
        nowPlayingPresented && !settingsPresented;
}

public static class NowPlayingPresentationPolicy
{
    public const double DismissDuration = 0.30;

    public static bool AcceptsInteraction(bool isPresented, bool settingsPresented) =>
        isPresented && !settingsPresented;

    public static bool IsAccessibilityVisible(bool isPresented, bool settingsPresented) =>
        AcceptsInteraction(isPresented, settingsPresented);
}

public static class SidebarGlassPolicy
{
    public const bool UsesLiquidGlass = true;
    public const bool TouchesTopLeadingAndBottomEdges = true;
}

public static class StationCardHitPolicy
{
    public const bool ClipsOverflow = true;
}

/// <summary>
/// Win11 caption lives in the native title bar on the right.
/// Do not draw fake macOS traffic lights.
/// </summary>
public enum CaptionSide
{
    Left,
    Right
}

public static class CaptionPolicy
{
    public const bool UsesNativeCaptionButtons = true;
    public const bool ReparentsStandardButtons = false;
    public const bool DrawsFakeTrafficLights = false;
    public const CaptionSide PreferredSide = CaptionSide.Right;
}

public static class WindowChromeMetrics
{
    public const double SidebarOuterInset = 0;
    /// <summary>Win11 caption is on the right; sidebar does not reserve a traffic-light pad.</summary>
    public const double CaptionClearanceWidth = 0;
    public const double CaptionDragHeight = 32;
    public const double MinimumWidth = 840;
    public const double MinimumHeight = 600;
    public const double DefaultWidth = 1280;
    public const double DefaultHeight = 800;
}

public static class SidebarRowHitPolicy
{
    public const bool UsesFullRowHitTarget = true;
}

public static class SongGridMetrics
{
    public const double MinCard = 148;
    public const double MaxCard = 176;
    public const double Spacing = 18;
    public const double Aspect = 3.0 / 4.0;
}

public static class PlayerDockMetrics
{
    public const double Height = AppleMusicTokens.CapsuleHeight;
    public const double Art = 40;
    public const double Icon = 28;
    public const double Play = 32;
    public const double ProgressHorizontalInset = Height / 2;
    public const double ProgressTopInset = 0;
    public const double ProgressHeight = 3;
    public const double CapsuleWidth = AppleMusicTokens.CapsuleWidth;
}
