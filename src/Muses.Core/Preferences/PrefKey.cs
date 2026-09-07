namespace Muses.Core.Preferences;

public static class PrefKey
{
    public const string NowPlayingMode = "muses.nowPlayingMode";
    public const string NowPlayingLyricsMode = "muses.nowPlaying.lyricsMode";
    public const string Theme = "muses.theme";
    public const string Language = "muses.language";
    public const string Volume = "muses.playback.volume";
    public const string CrossfadeSeconds = "muses.playback.crossfadeSeconds";
    public const string ReplayGainEnabled = "muses.playback.replayGainEnabled";
    public const string ResumeAfterVideo = "muses.playback.resumeAfterVideo";
    public const string SidebarCollapsed = "muses.sidebarCollapsed";
    public const string WebHomeEnabled = "muses.webHome.enabled";
    public const string WebHomeConsentVersion = "muses.webHome.consentVersion";
    public const string FfTray = "muses.ff.tray";
    public const string FfMiniPlayer = "muses.ff.miniPlayer";
    public const string FfDesktopLyrics = "muses.ff.desktopLyrics";
    public const string FfGlobalHotkeys = "muses.ff.globalHotkeys";
}

public static class FeatureFlagDefaults
{
    public static readonly IReadOnlyDictionary<string, bool> EnabledByDefault = new Dictionary<string, bool>
    {
        ["muses.ff.smartHistory"] = true,
        ["muses.ff.sessions"] = true,
        ["muses.ff.advancedQueue"] = true,
        ["muses.ff.inbox"] = true,
        ["muses.ff.notes"] = true,
        ["muses.ff.context"] = true,
        ["muses.ff.automation"] = true,
        ["muses.ff.audioNerd"] = true,
        ["muses.ff.focusMode"] = true,
        ["muses.ff.discovery"] = true,
        ["muses.ff.situationalNew"] = true,
        [PrefKey.FfTray] = true
    };
}

public enum AppTheme
{
    Dark,
    Light,
    System
}

public static class AppThemeCodec
{
    public static AppTheme Parse(string? raw) => raw switch
    {
        "light" => AppTheme.Light,
        "system" => AppTheme.System,
        _ => AppTheme.Dark
    };

    public static string Wire(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.System => "system",
        _ => "dark"
    };
}

public enum SidebarSection
{
    Search,
    Home,
    Discover,
    Recently,
    Songs,
    Albums,
    Artists,
    Playlists,
    History,
    Inbox
}
