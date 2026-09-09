using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using Muses.Core.Advanced;
using Muses.Core.Chrome;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Core.Preferences;
using Muses.Core.System;
using Muses.Core.Update;
using Muses.Core.YouTube;
using Muses.Infrastructure.Advanced;
using Muses.Infrastructure.Update;
using Muses.Infrastructure.YTDlp;
using Muses.Infrastructure.Account;
using Muses.Infrastructure.Playback;
using Muses.Platform.Windows;
using Muses.App.Services;

namespace Muses.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _sidebarCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyPropertyChangedFor(nameof(IsDiscover))]
    [NotifyPropertyChangedFor(nameof(IsRecently))]
    [NotifyPropertyChangedFor(nameof(IsSongs))]
    [NotifyPropertyChangedFor(nameof(IsAlbums))]
    [NotifyPropertyChangedFor(nameof(IsArtists))]
    [NotifyPropertyChangedFor(nameof(IsHistory))]
    [NotifyPropertyChangedFor(nameof(IsPlaylists))]
    [NotifyPropertyChangedFor(nameof(IsSearch))]
    [NotifyPropertyChangedFor(nameof(IsInbox))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsOverview))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsOverview))]
    [NotifyPropertyChangedFor(nameof(IsReleaseDetail))]
    [NotifyPropertyChangedFor(nameof(IsArtistsOverview))]
    [NotifyPropertyChangedFor(nameof(IsArtistDetail))]
    private SidebarSection _selectedSection = SidebarSection.Home;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistDetail))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsOverview))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsDetailVisible))]
    private PlaylistEntity? _selectedPlaylist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistDetail))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsOverview))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsDetailVisible))]
    private YouTubeImportEntity? _selectedYouTubeImport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReleaseDetail))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsOverview))]
    private Muses.Core.Catalog.CatalogReleaseProjection? _selectedRelease;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtistDetail))]
    [NotifyPropertyChangedFor(nameof(IsArtistsOverview))]
    private Muses.Core.Catalog.CatalogArtistProjection? _selectedArtist;

    [ObservableProperty] private IReadOnlyList<PlaylistEntity> _pinnedPlaylists = [];
    [ObservableProperty] private PlaylistDeletionSnapshot? _undoablePlaylistDeletion;

    [ObservableProperty] private bool _showAddChoiceSheet;
    [ObservableProperty] private bool _showNewPlaylistDialog;
    [ObservableProperty] private bool _showImportDialog;
    [ObservableProperty] private string _newPlaylistName = "";
    [ObservableProperty] private string _importPlaylistUrl = "";
    [ObservableProperty] private string? _importError;
    [ObservableProperty] private bool _importBusy;
    [ObservableProperty] private bool _settingsOpen;
    [ObservableProperty] private bool _pasteOpen;
    [ObservableProperty] private string _pasteUrl = "";
    [ObservableProperty] private string? _pasteError;
    [ObservableProperty] private bool _pasteBusy;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string? _nowPlayingTitle;
    [ObservableProperty] private string? _nowPlayingArtist;
    [ObservableProperty] private string? _nowPlayingArtworkUrl;
    [ObservableProperty] private string? _nowPlayingYouTubeId;
    [ObservableProperty] private string _nowPlayingSubtitle = "";
    [ObservableProperty] private bool _isCurrentTrackLiked;
    [ObservableProperty] private string _qualityLabel = "";
    [ObservableProperty] private bool _hasTrack;
    [ObservableProperty] private bool _shuffleOn;
    [ObservableProperty] private bool _repeatOn;
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private bool _isNowPlayingOpen;
    [ObservableProperty] private Muses.Core.NowPlaying.NowPlayingMode _nowPlayingMode = Muses.Core.NowPlaying.NowPlayingMode.Cover;
    [ObservableProperty] private Muses.Core.NowPlaying.NowPlayingLyricsMode _nowPlayingLyricsMode = Muses.Core.NowPlaying.NowPlayingLyricsMode.Inline;
    [ObservableProperty] private bool _isLyricsDrawerOpen;
    [ObservableProperty] private bool _isQueueDrawerOpen;
    [ObservableProperty] private bool _isVideoOverlayOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPositionFormatted))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private double _currentPositionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDurationFormatted))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private double _currentDurationSeconds;

    public string CurrentDurationFormatted => $"{(int)(CurrentDurationSeconds / 60)}:{(int)(CurrentDurationSeconds % 60):D2}";
    public double ProgressFraction => CurrentDurationSeconds > 0 ? Math.Clamp(CurrentPositionSeconds / CurrentDurationSeconds, 0, 1) : 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumePercent))]
    private float _currentVolume = 0.8f;

    public int VolumePercent => (int)Math.Round(CurrentVolume * 100);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeatOne))]
    [NotifyPropertyChangedFor(nameof(IsRepeatAll))]
    private RepeatMode _currentRepeatMode = RepeatMode.Off;

    public bool IsRepeatOne => CurrentRepeatMode == RepeatMode.One;
    public bool IsRepeatAll => CurrentRepeatMode == RepeatMode.All;

    // Wave 6 modals & state
    [ObservableProperty] private bool _isEQOpen;
    [ObservableProperty] private bool _webHomeEnabled;
    [ObservableProperty] private string _webHomeStatusText = "";
    [ObservableProperty] private bool _isAudioNerdOpen;
    [ObservableProperty] private bool _isFocusOpen;
    [ObservableProperty] private bool _isNotesOpen;
    [ObservableProperty] private bool _isSaveEQPresetOpen;
    [ObservableProperty] private string _newEQPresetName = "";

    private IReadOnlyList<EQBandViewModel> _eqBandViewModels = [];
    public IReadOnlyList<EQBandViewModel> EQBandViewModels
    {
        get => _eqBandViewModels;
        set => SetProperty(ref _eqBandViewModels, value);
    }
    [ObservableProperty] private IReadOnlyList<EQPresetEntity> _customEQPresets = [];
    [ObservableProperty] private IReadOnlyList<AudioInfoRow> _audioInfoRows = [];

    [ObservableProperty] private bool _isFocusActive;
    [ObservableProperty] private bool _isFocusQueueLocked;
    [ObservableProperty] private string _focusPhaseLabel = "Focusing";
    [ObservableProperty] private string _focusRemainingFormatted = "25:00";
    [ObservableProperty] private bool _focusPomodoroEnabled;
    [ObservableProperty] private bool _focusUntimed;
    [ObservableProperty] private bool _focusMinutes25 = true;
    [ObservableProperty] private bool _focusMinutes45;
    [ObservableProperty] private bool _focusMinutes60;
    [ObservableProperty] private bool _focusMinutes90;
    [ObservableProperty] private bool _focusExpiryPause = true;
    [ObservableProperty] private bool _focusExpiryKeepPlaying;
    [ObservableProperty] private bool _focusExpiryNotifyOnly;
    [ObservableProperty] private bool _focusLockQueue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBookmarks))]
    private IReadOnlyList<TrackBookmarkEntity> _currentTrackBookmarks = [];
    [ObservableProperty] private string _currentTrackNote = "";
    [ObservableProperty] private string _newBookmarkTitle = "";
    public bool HasBookmarks => CurrentTrackBookmarks.Count > 0;
    public string CurrentPositionFormatted => $"{(int)(CurrentPositionSeconds / 60)}:{(int)(CurrentPositionSeconds % 60):D2}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingInboxItems))]
    [NotifyPropertyChangedFor(nameof(IsInboxEmpty))]
    private IReadOnlyList<InboxItemEntity> _pendingInboxItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnoozedInboxItems))]
    [NotifyPropertyChangedFor(nameof(IsInboxEmpty))]
    private IReadOnlyList<InboxItemEntity> _snoozedInboxItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDecidedInboxItems))]
    private string _decidedInboxSummary = "";

    public bool HasPendingInboxItems => PendingInboxItems.Count > 0;
    public bool HasSnoozedInboxItems => SnoozedInboxItems.Count > 0;
    public bool HasDecidedInboxItems => !string.IsNullOrEmpty(DecidedInboxSummary);
    public bool IsInboxEmpty => PendingInboxItems.Count == 0 && SnoozedInboxItems.Count == 0;

    public PlaybackService? Playback { get; private set; }
    public CommandRegistry Commands { get; } = new();
    private LibraryService? _library;
    private IYTDlpBridge? _ytdlp;
    public LibraryService? Library => _library;
    public PlaylistService? PlaylistService { get; private set; }
    public YouTubeImportService? YouTubeImportService { get; private set; }
    public Muses.Infrastructure.Lyrics.LyricsService? Lyrics { get; private set; }
    public Muses.Infrastructure.History.HistoryService? History { get; private set; }
    public EQService? EQ { get; private set; }
    public bool IsEqAvailable { get; private set; } = true;
    public string EqUnavailableReason { get; private set; } = "";
    public FocusService? Focus { get; private set; }
    public NotesService? Notes { get; private set; }
    public InboxService? Inbox { get; private set; }
    public AutomationService? Automation { get; private set; }

    // Update Checker
    public IUpdateChecker? UpdateChecker { get; private set; }
    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private string _updateStatusMessage = "";
    [ObservableProperty] private bool _hasAvailableUpdate;
    [ObservableProperty] private AppUpdateInfo? _availableUpdateInfo;
    [ObservableProperty] private bool _autoCheckUpdates = true;

    private DispatcherTimer? _playbackTimer;
    private YouTubeVideoOverlaySession? _videoOverlaySession;
    private IYouTubeIFrameClient? _videoIFrameClient;
    private ProcessStreamEngine? _streamEngine;

    /// <summary>Live iframe client for the overlay host (WebView2 on Windows, degraded elsewhere).</summary>
    public IYouTubeIFrameClient? VideoIFrameClient => _videoIFrameClient;
    public IPreferences Preferences { get; private set; } = new MemoryPreferences();
    public YouTubeAccountService? Account { get; private set; }
    public WebHomeSessionController? WebHome { get; private set; }
    public WindowsTrayController? Tray { get; private set; }
    public WindowsSMTCService? Smtc { get; private set; }

    public bool ShowGuestBanner => Account is null || !Account.IsSignedIn;
    public string? AccountDisplayName => Account?.Profile?.DisplayName;
    public string? AccountErrorMessage => Account?.ErrorMessage;
    public bool IsAccountError => Account?.State == Muses.Core.Account.YouTubeAccountState.Error;
    public bool IsAccountSignedIn => Account?.IsSignedIn == true;
    public bool IsAccountSigningIn => Account?.State == Muses.Core.Account.YouTubeAccountState.SigningIn;

    public bool ReplayGainEnabled
    {
        get => Preferences.GetBool(PrefKey.ReplayGainEnabled, true);
        set
        {
            if (ReplayGainEnabled == value) return;
            Preferences.SetBool(PrefKey.ReplayGainEnabled, value);
            _streamEngine?.SetReplayGainEnabled(value);
            OnPropertyChanged(nameof(ReplayGainEnabled));
        }
    }

    public bool CloseToTray
    {
        get => Preferences.GetBool(PrefKey.CloseToTray, true);
        set
        {
            if (CloseToTray == value) return;
            Preferences.SetBool(PrefKey.CloseToTray, value);
            OnPropertyChanged(nameof(CloseToTray));
        }
    }

    public bool TrayEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfTray);
        set
        {
            if (TrayEnabled == value) return;
            Preferences.SetBool(PrefKey.FfTray, value);
            Tray?.SetEnabled(value);
            OnPropertyChanged(nameof(TrayEnabled));
        }
    }

    public bool MiniPlayerEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfMiniPlayer);
        set
        {
            if (MiniPlayerEnabled == value) return;
            Preferences.SetBool(PrefKey.FfMiniPlayer, value);
            OnPropertyChanged(nameof(MiniPlayerEnabled));
        }
    }

    public bool DesktopLyricsEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfDesktopLyrics);
        set
        {
            if (DesktopLyricsEnabled == value) return;
            Preferences.SetBool(PrefKey.FfDesktopLyrics, value);
            OnPropertyChanged(nameof(DesktopLyricsEnabled));
        }
    }

    /// <summary>
    /// Optional system-wide hotkeys (default off). In-window Ctrl+P / Ctrl+Left / Ctrl+Right
    /// are always handled by MainWindow when focused — this flag does not gate those.
    /// </summary>
    public bool GlobalHotkeysEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfGlobalHotkeys);
        set
        {
            if (GlobalHotkeysEnabled == value) return;
            Preferences.SetBool(PrefKey.FfGlobalHotkeys, value);
            OnPropertyChanged(nameof(GlobalHotkeysEnabled));
        }
    }
    public string LanguagePreference
    {
        get => Preferences.GetString(PrefKey.Language, "system");
        set
        {
            if (LanguagePreference == value) return;
            Preferences.SetString(PrefKey.Language, value);
            if (L10n.Source is SystemLanguageSource sys)
                sys.Preference = value;
            OnPropertyChanged(nameof(LanguagePreference));
            NotifyL10nLabels();
        }
    }

    public int LanguageSelectedIndex
    {
        get => LanguagePreference switch
        {
            "en" => 1,
            "zh" or "zh-Hans" => 2,
            _ => 0
        };
        set
        {
            LanguagePreference = value switch
            {
                1 => "en",
                2 => "zh",
                _ => "system"
            };
            OnPropertyChanged(nameof(LanguageSelectedIndex));
        }
    }

    public string ThemePreference
    {
        get => Preferences.GetString(PrefKey.Theme, "dark");
        set
        {
            if (ThemePreference == value) return;
            Preferences.SetString(PrefKey.Theme, value);
            OnPropertyChanged(nameof(ThemePreference));
            OnPropertyChanged(nameof(ThemeSelectedIndex));
        }
    }

    public int ThemeSelectedIndex
    {
        get => ThemePreference == "system" ? 1 : 0;
        set
        {
            ThemePreference = value == 1 ? "system" : "dark";
            OnPropertyChanged(nameof(ThemeSelectedIndex));
        }
    }

    public string AudioQuality
    {
        get => Preferences.GetString(PrefKey.AudioQuality, "best");
        set
        {
            if (AudioQuality == value) return;
            Preferences.SetString(PrefKey.AudioQuality, value);
            _streamEngine?.SetStreamQuality(ProcessStreamEngine.MapAudioQualityPref(value));
            OnPropertyChanged(nameof(AudioQuality));
            OnPropertyChanged(nameof(AudioQualitySelectedIndex));
        }
    }

    public int AudioQualitySelectedIndex
    {
        get => AudioQuality switch
        {
            "high" => 1,
            "medium" => 2,
            _ => 0
        };
        set
        {
            AudioQuality = value switch
            {
                1 => "high",
                2 => "medium",
                _ => "best"
            };
            OnPropertyChanged(nameof(AudioQualitySelectedIndex));
        }
    }

    public bool IsCapsuleVisible => !IsNowPlayingOpen && !IsVideoOverlayOpen;

    public bool ResumeAfterVideo
    {
        get => Preferences.GetBool(PrefKey.ResumeAfterVideo, true);
        set
        {
            if (ResumeAfterVideo == value) return;
            Preferences.SetBool(PrefKey.ResumeAfterVideo, value);
            OnPropertyChanged(nameof(ResumeAfterVideo));
        }
    }
    public bool IsVideoEmbedAvailable => _videoIFrameClient?.IsAvailable ?? false;
    public string VideoEmbedUnavailableMessage => L10n.Tr(
        "In-app YouTube embed requires WebView2 (Windows). On this Mac build the embed surface is disabled — use Open in YouTube, or continue with native audio.",
        "应用内 YouTube 嵌入需要 WebView2（Windows）。当前 Mac 构建已禁用嵌入画面 — 请使用“在 YouTube 中打开”，或继续使用原生音频。");

    public double SidebarWidth =>
        SidebarCollapsed ? AppleMusicTokens.SidebarCollapsedWidth : AppleMusicTokens.SidebarWidth;

    /// <summary>macOS still draws traffic lights on the left; Win11 caption is on the right.</summary>
    public bool NeedsTrafficLightPad => OperatingSystem.IsMacOS();
    public double TrafficLightPadHeight => NeedsTrafficLightPad ? 22 : 0;

    public bool IsSearch => SelectedSection == SidebarSection.Search;
    public bool IsHome => SelectedSection == SidebarSection.Home;
    public bool IsDiscover => SelectedSection == SidebarSection.Discover;
    public bool IsRecently => SelectedSection == SidebarSection.Recently;
    public bool IsSongs => SelectedSection == SidebarSection.Songs;
    public bool IsAlbums => SelectedSection == SidebarSection.Albums;
    public bool IsArtists => SelectedSection == SidebarSection.Artists;
    public bool IsHistory => SelectedSection == SidebarSection.History;
    public bool IsPlaylists => SelectedSection == SidebarSection.Playlists;
    public bool IsInbox => SelectedSection == SidebarSection.Inbox;
    public bool IsPlaylistDetail => SelectedPlaylist is not null || SelectedYouTubeImport is not null;
    public bool IsPlaylistsOverview => IsPlaylists && !IsPlaylistDetail;
    public bool IsPlaylistsDetailVisible => IsPlaylists && IsPlaylistDetail;
    public bool IsReleaseDetail => IsAlbums && SelectedRelease is not null;
    public bool IsAlbumsOverview => IsAlbums && SelectedRelease is null;
    public bool IsArtistDetail => IsArtists && SelectedArtist is not null;
    public bool IsArtistsOverview => IsArtists && SelectedArtist is null;

    public Muses.Infrastructure.Discovery.HomeDiscoveryService? HomeDiscovery { get; private set; }
    public Muses.Infrastructure.Catalog.YouTubeCatalogService? Catalog { get; private set; }
    public Muses.Core.Recommendation.SituationalRecommendationService? Situational { get; private set; }
    public Muses.Infrastructure.Search.GlobalSearchService? GlobalSearch { get; private set; }

    public event Action<string?, Muses.Core.Search.GlobalSearchScope?>? RequestOpenSearchWindow;

    public string Wordmark => "Muses";
    public string SearchLabel => L10n.Tr("Search", "搜索");
    public string HomeLabel => L10n.Tr("Home", "首页");
    public string DiscoverLabel => L10n.Tr("Discover", "发现");
    public string LibraryLabel => L10n.Tr("Library", "资料库");
    public string RecentlyLabel => L10n.Tr("Recently", "最近");
    public string SongsLabel => L10n.Tr("Songs", "歌曲");
    public string AlbumsLabel => L10n.Tr("Albums", "专辑");
    public string ArtistsLabel => L10n.Tr("Artists", "艺术家");
    public string HistoryLabel => L10n.Tr("History", "历史记录");
    public string PlaylistsLabel => L10n.Tr("Playlists", "歌单");
    public string AllPlaylistsLabel => L10n.Tr("All Playlists", "全部歌单");
    public string AddPlaylistLabel => L10n.Tr("Add Playlist", "添加歌单");
    public string NewPlaylistLabel => L10n.Tr("New Playlist", "新建歌单");
    public string NewPlaylistDescLabel => L10n.Tr("Create an empty Muses playlist", "创建一个空的 Muses 歌单");
    public string ImportYouTubeLabel => L10n.Tr("Import YouTube Playlist", "导入 YouTube 歌单");
    public string ImportYouTubeDescLabel => L10n.Tr("Paste a YouTube or YouTube Music playlist link", "粘贴 YouTube 或 YouTube Music 歌单链接");
    public string PlaylistNameWatermark => L10n.Tr("Playlist name", "歌单名称");
    public string CreateLabel => L10n.Tr("Create", "创建");
    public string ImportActionLabel => L10n.Tr("Import", "导入");
    public string ImportNoticeLabel => L10n.Tr(
        "Supports YouTube playlist links (with list= parameter). Importing is for personal use only, comply with YouTube's Terms of Service and local laws.",
        "支持 YouTube 歌单链接(含 list= 参数)。导入仅个人使用，遵守 YouTube 服务条款与当地法律。");
    public string SettingsLabel => L10n.Tr("Settings", "设置");
    public string CollapseLabel => L10n.Tr("Collapse Sidebar", "折叠边栏");
    public string ExpandLabel => L10n.Tr("Expand Sidebar", "展开边栏");
    public string NotPlayingLabel => L10n.Tr("Not Playing", "未在播放");
    public string PublicDiscoveryLabel => L10n.Tr("Public discovery", "公共发现");
    public string GuestBannerTitle => L10n.Tr("Make Home yours", "让首页更懂你");
    public string GuestBannerBody => L10n.Tr(
        "Sign in to personalize recommendations. Playback never waits on an account.",
        "登录后即可个性化推荐。播放从不依赖账号。");
    public string SignInLabel => L10n.Tr("Sign In", "登录");
    public string SignOutLabel => L10n.Tr("Sign Out", "退出登录");
    public string SettingsGeneralTitle => L10n.Tr("General Settings", "通用设置");
    public string SettingsLanguageLabel => L10n.Tr("Language", "语言");
    public string SettingsCloseToTrayLabel => L10n.Tr("Close button minimizes to System Tray", "关闭按钮最小化到系统托盘");
    public string SettingsTrayLabel => L10n.Tr("Show tray / notification area icon", "显示托盘 / 通知区域图标");
    public string SettingsPlaybackTitle => L10n.Tr("Playback Settings", "播放设置");
    public string SettingsResumeAfterVideoLabel => L10n.Tr("Resume music playback when video overlay closes", "关闭视频浮层后恢复音乐播放");
    public string SettingsReplayGainLabel => L10n.Tr("Volume normalization (ReplayGain)", "音量标准化（ReplayGain）");
    public string SettingsYouTubeTitle => L10n.Tr("YouTube Account", "YouTube 账号");
    public string SettingsGoogleSignInLabel => L10n.Tr("Google Account Sign-In", "Google 账号登录");
    public string SettingsGoogleSignInBody => L10n.Tr(
        "Sign in to personalize Home and sync your liked tracks and playlists.",
        "登录后即可个性化首页并同步喜欢的歌曲与歌单。");

    public string SettingsAudioQualityTitle => L10n.Tr("Audio Quality", "音频质量");
    public string SettingsPreferredStreamLabel => L10n.Tr("Preferred Stream Quality", "首选流质量");
    public string SettingsAppearanceTitle => L10n.Tr("Appearance", "外观");
    public string SettingsThemeLabel => L10n.Tr("Theme", "主题");
    public string SettingsAccentLabel => L10n.Tr("Accent Color", "强调色");
    public string SettingsLyricsTitle => L10n.Tr("Lyrics Settings", "歌词设置");
    public string SettingsLyricsProviderNote => L10n.Tr(
        "Lyrics are fetched from LRCLIB (synced and plain). No alternate provider is wired yet.",
        "歌词来自 LRCLIB（支持逐行与纯文本）。尚未接入其他提供方。");
    public string SettingsDesktopTitle => L10n.Tr("Desktop Tools & Widgets", "桌面工具与小组件");
    public string SettingsMiniPlayerLabel => L10n.Tr("Mini Player", "迷你播放器");
    public string SettingsMiniPlayerBody => L10n.Tr(
        "Compact floating player window with always-on-top mode.",
        "可置顶的紧凑浮动播放器窗口。");
    public string SettingsDesktopLyricsLabel => L10n.Tr("Desktop Lyrics", "桌面歌词");
    public string SettingsDesktopLyricsBody => L10n.Tr(
        "Transparent, draggable desktop lyrics banner on top of other windows.",
        "可拖动的透明桌面歌词条，置于其他窗口之上。");
    public string SettingsGlobalHotkeysLabel => L10n.Tr(
        "System-wide transport hotkeys (experimental, default off)",
        "系统级播放快捷键（实验性，默认关闭）");
    public string SettingsGlobalHotkeysBody => L10n.Tr(
        "In-window Ctrl+P / Ctrl+Left / Ctrl+Right always work when Muses is focused. System-wide hooks stay off unless enabled here.",
        "Muses 聚焦时窗口内 Ctrl+P / Ctrl+Left / Ctrl+Right 始终可用。系统级钩子仅在此处开启后生效。");
    public string SettingsWebHomeTitle => L10n.Tr("Web Home (opt-in)", "Web Home（需同意）");
    public string SettingsWebHomeBody => L10n.Tr(
        "Uses an isolated helper process with an ephemeral cookie jar deleted on exit. Off by default; requires consent. No scraping runs in the Muses UI process.",
        "使用隔离的辅助进程与退出即删的临时 Cookie。默认关闭，需同意。Muses UI 进程内不进行抓取。");
    public string SettingsCatGeneral => L10n.Tr("General", "通用");
    public string SettingsCatPlayback => L10n.Tr("Playback", "播放");
    public string SettingsCatQuality => L10n.Tr("Audio Quality", "音频质量");
    public string SettingsCatAppearance => L10n.Tr("Appearance", "外观");
    public string SettingsCatYouTube => L10n.Tr("YouTube", "YouTube");
    public string SettingsCatLyrics => L10n.Tr("Lyrics", "歌词");
    public string SettingsCatDesktop => L10n.Tr("Desktop", "桌面");
    public string SettingsCatAbout => L10n.Tr("About", "关于");
    public string SettingsEngineStatusTitle => L10n.Tr("Stream engine", "流引擎");
    public string SettingsEngineStatusBody => L10n.Tr(
        "Uses bundled yt-dlp + mpv when available on PATH or under resources/.",
        "在 PATH 或 resources/ 可用时使用自带的 yt-dlp 与 mpv。");
    public string SettingsReplayGainUnavailableNote => L10n.Tr(
        "When enabled, track ReplayGain (dB) is applied via mpv volume adjustment when metadata is present.",
        "开启后，若曲目含 ReplayGain（dB）元数据，将通过 mpv 音量调整应用。");
    public string SettingsTokensPrivacyLabel => L10n.Tr(
        "Tokens stay in the OS credential locker on this device. No telemetry.",
        "令牌保存在本机操作系统凭证保管库中。无遥测。");
    public string NewReleasesLabel => L10n.Tr("New Releases", "新发行");
    public string SeeAllLabel => L10n.Tr("See All", "查看全部");
    public string PasteTitle => L10n.Tr("Paste YouTube Link", "粘贴 YouTube 链接");
    public string ImportLabel => L10n.Tr("Play", "播放");
    public string CancelLabel => L10n.Tr("Cancel", "取消");
    public string PauseLabel => L10n.Tr("Pause", "暂停");
    public string PlayLabel => L10n.Tr("Play", "播放");

    public void Attach(
        PlaybackService playback,
        LibraryService library,
        IYTDlpBridge ytdlp,
        PlaylistService? playlistService = null,
        YouTubeImportService? youtubeImportService = null,
        Muses.Infrastructure.Discovery.HomeDiscoveryService? homeDiscovery = null,
        Muses.Infrastructure.Catalog.YouTubeCatalogService? catalog = null,
        Muses.Core.Recommendation.SituationalRecommendationService? situational = null,
        Muses.Infrastructure.Search.GlobalSearchService? globalSearch = null,
        Muses.Infrastructure.Lyrics.LyricsService? lyrics = null,
        Muses.Infrastructure.History.HistoryService? history = null,
        EQService? eq = null,
        FocusService? focus = null,
        NotesService? notes = null,
        InboxService? inbox = null,
        AutomationService? automation = null,
        IPreferences? preferences = null,
        YouTubeAccountService? account = null,
        WindowsSMTCService? smtc = null,
        WindowsTrayController? tray = null,
        WebHomeSessionController? webHome = null,
        ProcessStreamEngine? streamEngine = null)
    {
        Playback = playback;
        Preferences = preferences ?? new MemoryPreferences();
        Account = account;
        Smtc = smtc;
        Tray = tray;
        WebHome = webHome;
        _streamEngine = streamEngine;
        if (_streamEngine is not null)
        {
            _streamEngine.SetStreamQuality(ProcessStreamEngine.MapAudioQualityPref(AudioQuality));
            _streamEngine.SetReplayGainEnabled(ReplayGainEnabled);
        }
        WebHomeEnabled = Preferences.GetBool(PrefKey.WebHomeEnabled, false);
        RefreshWebHomeStatus();
        if (WebHome is not null)
            WebHome.StatusChanged += () => Dispatcher.UIThread.Post(RefreshWebHomeStatus);
        if (Account is not null)
            Account.Changed += () => Dispatcher.UIThread.Post(NotifyAccountChanged);
        var lang = Preferences.GetString(PrefKey.Language, "system");
        if (L10n.Source is SystemLanguageSource sys)
            sys.Preference = lang;
        SidebarCollapsed = Preferences.GetBool(PrefKey.SidebarCollapsed, false);
        var modeRaw = Preferences.GetString(PrefKey.NowPlayingMode, "cover");
        NowPlayingMode = modeRaw == "vinyl"
            ? Muses.Core.NowPlaying.NowPlayingMode.Vinyl
            : Muses.Core.NowPlaying.NowPlayingMode.Cover;
        _videoIFrameClient = YouTubeEmbedHostFactory.CreateClient();
        _videoOverlaySession = new YouTubeVideoOverlaySession(playback, _videoIFrameClient, Preferences);
        _library = library;
        _ytdlp = ytdlp;
        PlaylistService = playlistService;
        YouTubeImportService = youtubeImportService;
        HomeDiscovery = homeDiscovery;
        Catalog = catalog;
        Situational = situational;
        GlobalSearch = globalSearch;
        Lyrics = lyrics;
        History = history;
        EQ = eq;
        IsEqAvailable = playback.EngineSupportsEq;
        EqUnavailableReason = IsEqAvailable
            ? ""
            : L10n.Tr("Equalizer is unavailable because the audio engine cannot apply EQ filters.", "均衡器不可用：当前音频引擎无法应用 EQ 滤镜。");
        OnPropertyChanged(nameof(IsEqAvailable));
        OnPropertyChanged(nameof(EqUnavailableReason));
        Focus = focus;
        Notes = notes;
        Inbox = inbox;
        Automation = automation;

        if (EQ != null)
        {
            CustomEQPresets = EQ.GetCustomPresets();
            RefreshEQBands();
            EQ.BandsChanged += _ => Dispatcher.UIThread.Post(RefreshEQBands);
            EQ.PresetChanged += _ => Dispatcher.UIThread.Post(() =>
            {
                CustomEQPresets = EQ.GetCustomPresets();
                RefreshAudioInfo();
            });
        }

        if (Focus != null)
        {
            Focus.StateChanged += () => Dispatcher.UIThread.Post(RefreshFocusState);
        }

        if (Notes != null)
        {
            Notes.Changed += () => Dispatcher.UIThread.Post(RefreshNotesState);
        }

        if (Inbox != null)
        {
            Inbox.Changed += () => Dispatcher.UIThread.Post(RefreshInboxState);
            RefreshInboxState();
        }

        Commands.Register(CommandRegistry.TogglePlayback, playback.Toggle);
        Commands.Register(CommandRegistry.Next, playback.Next);
        Commands.Register(CommandRegistry.Previous, playback.Previous);
        Commands.Register(CommandRegistry.PasteYouTube, () => PasteOpen = true);
        playback.EventBus.EventPosted += _ => Dispatcher.UIThread.Post(RefreshPlayback);
        RefreshPlayback();
        RefreshSidebar();

        if (Lyrics is not null)
        {
            Lyrics.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(Lyrics));
            });
        }

        if (History is not null)
        {
            History.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(History));
            });
        }

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _playbackTimer.Tick += (_, _) =>
        {
            if (Playback is not null && Playback.State.IsPlaying)
            {
                CurrentPositionSeconds = Playback.State.Position;
                Lyrics?.UpdatePosition(CurrentPositionSeconds);
            }
        };
        _playbackTimer.Start();

        if (HomeDiscovery is not null)
        {
            HomeDiscovery.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(HomeDiscovery));
            });
            HomeDiscovery.Load();
        }

        if (Catalog is not null)
        {
            Catalog.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(Catalog));
            });
        }
    }

    [RelayCommand]
    public void OpenSearchWindow(string? query = null)
    {
        RequestOpenSearchWindow?.Invoke(query, null);
    }

    public void OpenSearchWindowWithScope(string? query, Muses.Core.Search.GlobalSearchScope scope)
    {
        RequestOpenSearchWindow?.Invoke(query, scope);
    }

    [RelayCommand]
    public void SelectRelease(Muses.Core.Catalog.CatalogReleaseProjection release)
    {
        SelectedSection = SidebarSection.Albums;
        SelectedRelease = release;
    }

    [RelayCommand]
    public void CloseReleaseDetail()
    {
        SelectedRelease = null;
    }

    [RelayCommand]
    public void SelectArtist(Muses.Core.Catalog.CatalogArtistProjection artist)
    {
        SelectedSection = SidebarSection.Artists;
        SelectedArtist = artist;
    }

    [RelayCommand]
    public void CloseArtistDetail()
    {
        SelectedArtist = null;
    }

    public void NavigateToRelease(string stableId)
    {
        SelectedSection = SidebarSection.Albums;
        var rel = Catalog?.Release(stableId);
        if (rel is not null) SelectedRelease = rel;
    }

    public void NavigateToArtist(string stableId)
    {
        SelectedSection = SidebarSection.Artists;
        var art = Catalog?.Artist(stableId);
        if (art is not null) SelectedArtist = art;
    }

    public void RefreshPlayback()
    {
        if (Playback is null) return;
        var track = Playback.State.Track;
        HasTrack = track is not null;
        IsPlaying = Playback.State.IsPlaying;
        NowPlayingTitle = track?.Title;
        NowPlayingArtist = track?.Artist;
        NowPlayingArtworkUrl = track?.ArtworkUrl;
        NowPlayingYouTubeId = track?.YouTubeId;
        CurrentDurationSeconds = track?.DurationSeconds ?? 0;
        CurrentPositionSeconds = Playback.State.Position;
        CurrentVolume = Playback.Volume;
        ShuffleOn = Playback.Queue.Shuffle;
        CurrentRepeatMode = Playback.Queue.RepeatMode;
        RepeatOn = CurrentRepeatMode != RepeatMode.Off;

        if (track is not null)
        {
            var ent = _library?.Get(track.Id);
            IsCurrentTrackLiked = ent?.Liked ?? track.Liked;
            NowPlayingSubtitle = !string.IsNullOrEmpty(track.AlbumTitle)
                ? $"{track.Artist} — {track.AlbumTitle}"
                : track.Artist;
            QualityLabel = track.IsLossless
                ? L10n.Tr("Lossless", "无损")
                : (track.Codec is not null && !string.IsNullOrWhiteSpace(track.Codec) && !track.Codec.Equals("native", StringComparison.OrdinalIgnoreCase) ? track.Codec : "");
        }
        else
        {
            IsCurrentTrackLiked = false;
            NowPlayingSubtitle = "";
            QualityLabel = "";
        }

        Lyrics?.SetTrack(track);
        Lyrics?.UpdatePosition(CurrentPositionSeconds);

        RefreshAudioInfo();
        RefreshNotesState();

        OnPropertyChanged(nameof(NotPlayingLabel));
    }

    [RelayCommand]
    public void ToggleCurrentTrackLike()
    {
        var track = Playback?.State.Track;
        if (track is null || _library is null) return;
        _library.ToggleLike(track.Id);
        var ent = _library.Get(track.Id);
        IsCurrentTrackLiked = ent?.Liked ?? !IsCurrentTrackLiked;
    }

    [RelayCommand]
    public void PlayNextCurrentTrack()
    {
        var track = Playback?.State.Track;
        if (track is not null && Playback is not null)
        {
            Playback.Queue.PlayNext(track);
        }
    }

    [RelayCommand]
    public void AddCurrentTrackToQueue()
    {
        var track = Playback?.State.Track;
        if (track is not null && Playback is not null)
        {
            Playback.Queue.AddToQueue(track);
        }
    }

    [RelayCommand]
    public void OpenCurrentTrackOnYouTube()
    {
        var ytid = Playback?.State.Track?.YouTubeId;
        if (!string.IsNullOrEmpty(ytid))
        {
            try
            {
                var url = $"https://youtu.be/{ytid}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    public void ToggleNowPlaying()
    {
        if (!HasTrack && !IsNowPlayingOpen) return;
        IsNowPlayingOpen = !IsNowPlayingOpen;
        if (IsNowPlayingOpen)
        {
            IsLyricsDrawerOpen = false;
            IsQueueDrawerOpen = false;
        }
        OnPropertyChanged(nameof(IsCapsuleVisible));
    }

    [RelayCommand]
    public void CloseNowPlaying()
    {
        IsNowPlayingOpen = false;
        OnPropertyChanged(nameof(IsCapsuleVisible));
    }

    [RelayCommand]
    public void ToggleNowPlayingMode()
    {
        NowPlayingMode = NowPlayingMode == Muses.Core.NowPlaying.NowPlayingMode.Cover
            ? Muses.Core.NowPlaying.NowPlayingMode.Vinyl
            : Muses.Core.NowPlaying.NowPlayingMode.Cover;
        Preferences.SetString(PrefKey.NowPlayingMode,
            NowPlayingMode == Muses.Core.NowPlaying.NowPlayingMode.Vinyl ? "vinyl" : "cover");
    }

    [RelayCommand]
    public void ToggleNowPlayingLyricsMode()
    {
        NowPlayingLyricsMode = NowPlayingLyricsMode == Muses.Core.NowPlaying.NowPlayingLyricsMode.Inline
            ? Muses.Core.NowPlaying.NowPlayingLyricsMode.Fullscreen
            : Muses.Core.NowPlaying.NowPlayingLyricsMode.Inline;
    }

    [RelayCommand]
    public void ToggleLyricsDrawer()
    {
        IsLyricsDrawerOpen = !IsLyricsDrawerOpen;
        if (IsLyricsDrawerOpen) IsQueueDrawerOpen = false;
    }

    [RelayCommand]
    public void CloseLyricsDrawer()
    {
        IsLyricsDrawerOpen = false;
    }

    [RelayCommand]
    public void ToggleQueueDrawer()
    {
        IsQueueDrawerOpen = !IsQueueDrawerOpen;
        if (IsQueueDrawerOpen) IsLyricsDrawerOpen = false;
    }

    [RelayCommand]
    public void CloseQueueDrawer()
    {
        IsQueueDrawerOpen = false;
    }

    [RelayCommand]
    public void ToggleVideoOverlay()
    {
        if (!IsVideoOverlayOpen)
        {
            if (string.IsNullOrEmpty(NowPlayingYouTubeId)) return;
            // Opening video closes drawers; capsule hides via IsCapsuleVisible.
            IsLyricsDrawerOpen = false;
            IsQueueDrawerOpen = false;
            _videoOverlaySession?.Open(NowPlayingYouTubeId);
            IsVideoOverlayOpen = true;
            OnPropertyChanged(nameof(IsCapsuleVisible));
            OnPropertyChanged(nameof(IsVideoEmbedAvailable));
        }
        else
        {
            CloseVideoOverlay();
        }
    }

    [RelayCommand]
    public void CloseVideoOverlay()
    {
        _videoOverlaySession?.Close();
        IsVideoOverlayOpen = false;
        OnPropertyChanged(nameof(IsCapsuleVisible));
    }

    [RelayCommand]
    public void SeekToSeconds(double seconds)
    {
        Playback?.Seek(seconds);
        CurrentPositionSeconds = seconds;
        Lyrics?.UpdatePosition(seconds);
    }

    [RelayCommand]
    public void ChangeVolume(double val)
    {
        Playback?.SetVolume((float)val);
        CurrentVolume = Playback?.Volume ?? (float)val;
    }

    [RelayCommand]
    public void RemoveQueueItem(int index)
    {
        // Legacy: collection Items index only. Prefer RemoveUpcomingRow for drawer rows.
        Playback?.Queue.RemoveItem(index);
        OnPropertyChanged(nameof(Playback));
    }

    public void RemoveUpcomingRow(QueuePresentationRow row)
    {
        if (Playback is null) return;
        QueuePresentation.TryRemove(Playback.Queue, row);
        OnPropertyChanged(nameof(Playback));
    }

    public void MoveUpNextRow(int from, int to)
    {
        if (Playback is null) return;
        QueuePresentation.TryMoveUpNext(Playback.Queue, from, to);
        OnPropertyChanged(nameof(Playback));
    }

    [RelayCommand]
    public void ClearUpNext()
    {
        Playback?.Queue.ClearUpNext();
        OnPropertyChanged(nameof(Playback));
    }

    // MARK: - Wave 6 Advanced Features (EQ, Audio Nerd, Focus, Notes, Inbox)

    public void RefreshAudioInfo()
    {
        var track = Playback?.State.Track;
        AudioInfoRows = AudioInfoModel.BuildRows(track, null, EQ?.ActivePresetName, CurrentVolume);
    }

    public void RefreshNotesState()
    {
        var track = Playback?.State.Track;
        if (track != null && Notes != null)
        {
            CurrentTrackNote = Notes.GetNote(track.Id.ToString())?.Content ?? "";
            CurrentTrackBookmarks = Notes.GetBookmarks(track.Id.ToString());
        }
        else
        {
            CurrentTrackNote = "";
            CurrentTrackBookmarks = [];
        }
    }

    public void RefreshEQBands()
    {
        if (EQ == null) return;
        var bands = EQ.ActiveBands;
        var viewModels = new List<EQBandViewModel>(bands.Count);
        for (int i = 0; i < bands.Count; i++)
        {
            viewModels.Add(new EQBandViewModel(i, bands[i].Frequency, bands[i].Gain, (idx, g) =>
            {
                EQ.SetBandGain(idx, g);
            }));
        }
        EQBandViewModels = viewModels;
    }

    public void RefreshFocusState()
    {
        if (Focus == null) return;
        IsFocusActive = Focus.IsActive;
        IsFocusQueueLocked = Focus.IsQueueLocked;
        FocusPhaseLabel = Focus.IsPomodoro
            ? (Focus.PomodoroPhase == FocusPomodoroPhase.Focus ? L10n.Tr("Focus", "专注") : L10n.Tr("Break", "休息"))
            : L10n.Tr("Focusing", "专注中");
        FocusRemainingFormatted = Focus.RemainingFormatted;
    }

    public void RefreshInboxState()
    {
        if (Inbox == null) return;
        PendingInboxItems = Inbox.GetPendingItems();
        SnoozedInboxItems = Inbox.GetSnoozedItems();
        var decided = Inbox.GetDecidedItems();
        var accepted = decided.Count(i => i.State == InboxState.Accepted);
        var rejected = decided.Count(i => i.State == InboxState.Rejected);
        DecidedInboxSummary = (accepted > 0 || rejected > 0)
            ? L10n.Tr($"{accepted} accepted · {rejected} rejected", $"已接受 {accepted} · 已拒绝 {rejected}")
            : "";
    }

    [RelayCommand]
    public void ToggleEQ()
    {
        IsEQOpen = !IsEQOpen;
        if (IsEQOpen) RefreshEQBands();
    }

    [RelayCommand]
    public void CloseEQ() => IsEQOpen = false;

    [RelayCommand]
    public void ResetEQ()
    {
        EQ?.Reset();
        RefreshEQBands();
    }

    [RelayCommand]
    public void SelectEQPreset(string presetName)
    {
        EQ?.SelectPreset(presetName);
        RefreshEQBands();
    }

    [RelayCommand]
    public void OpenSaveEQPreset()
    {
        NewEQPresetName = "";
        IsSaveEQPresetOpen = true;
    }

    [RelayCommand]
    public void SaveEQPreset()
    {
        if (!string.IsNullOrWhiteSpace(NewEQPresetName) && EQ != null)
        {
            EQ.SaveCustomPreset(NewEQPresetName.Trim());
            CustomEQPresets = EQ.GetCustomPresets();
            IsSaveEQPresetOpen = false;
        }
    }

    [RelayCommand]
    public void CloseSaveEQPreset() => IsSaveEQPresetOpen = false;

    [RelayCommand]
    public void ToggleAudioNerd()
    {
        IsAudioNerdOpen = !IsAudioNerdOpen;
        if (IsAudioNerdOpen) RefreshAudioInfo();
    }

    [RelayCommand]
    public void CloseAudioNerd() => IsAudioNerdOpen = false;

    [RelayCommand]
    public void OpenEQFromAudioNerd()
    {
        IsAudioNerdOpen = false;
        IsEQOpen = true;
        RefreshEQBands();
    }

    [RelayCommand]
    public void ToggleFocus()
    {
        IsFocusOpen = !IsFocusOpen;
        if (IsFocusOpen) RefreshFocusState();
    }

    [RelayCommand]
    public void CloseFocus() => IsFocusOpen = false;

    [RelayCommand]
    public void StartFocus()
    {
        if (Focus == null) return;
        int? minutes = FocusUntimed ? null : (FocusMinutes25 ? 25 : (FocusMinutes45 ? 45 : (FocusMinutes60 ? 60 : 90)));
        var expiry = FocusExpiryPause ? FocusExpiration.Pause : (FocusExpiryKeepPlaying ? FocusExpiration.KeepPlaying : FocusExpiration.NotifyOnly);
        Focus.Start(minutes, FocusLockQueue, expiry, FocusPomodoroEnabled);
        RefreshFocusState();
    }

    [RelayCommand]
    public void EndFocus()
    {
        Focus?.Stop(completedByTimer: false);
        RefreshFocusState();
    }

    [RelayCommand]
    public void ToggleNotes()
    {
        IsNotesOpen = !IsNotesOpen;
        if (IsNotesOpen) RefreshNotesState();
    }

    [RelayCommand]
    public void CloseNotes()
    {
        var track = Playback?.State.Track;
        if (track != null && Notes != null)
        {
            Notes.SetTrackNote(track.Id.ToString(), CurrentTrackNote);
        }
        IsNotesOpen = false;
    }

    [RelayCommand]
    public void AddBookmarkAtCurrentPosition()
    {
        var track = Playback?.State.Track;
        if (track == null || Notes == null) return;
        var posMs = CurrentPositionSeconds * 1000.0;
        var title = string.IsNullOrWhiteSpace(NewBookmarkTitle) ? null : NewBookmarkTitle.Trim();
        Notes.AddBookmark(track.Id.ToString(), posMs, title);
        NewBookmarkTitle = "";
        RefreshNotesState();
    }

    [RelayCommand]
    public void DeleteBookmark(string id)
    {
        Notes?.RemoveBookmark(id);
        RefreshNotesState();
    }

    [RelayCommand]
    public void SeekToBookmark(double ms)
    {
        Playback?.Seek(ms / 1000.0);
        CurrentPositionSeconds = ms / 1000.0;
    }

    [RelayCommand]
    public void OpenInbox()
    {
        SelectedSection = SidebarSection.Inbox;
        RefreshInboxState();
    }

    [RelayCommand]
    public void PlayInboxItem(InboxItemEntity item)
    {
        if (Playback == null) return;
        var snap = new TrackSnapshot(
            Guid.Parse(item.TrackId), item.TrackTitle, item.Artist, item.AlbumTitle,
            item.DurationSeconds, item.YouTubeId, item.ArtworkUrl);
        Playback.PlayTrack(snap, [snap], QueueSource.Recently);
    }

    [RelayCommand]
    public void AcceptInboxItem(string id)
    {
        Inbox?.Accept(id, trackId =>
        {
            if (Guid.TryParse(trackId, out var g))
                _library?.ToggleLike(g);
        });
        RefreshInboxState();
    }

    [RelayCommand]
    public void RejectInboxItem(string id)
    {
        Inbox?.Reject(id);
        RefreshInboxState();
    }

    [RelayCommand]
    public void SnoozeInboxItemTomorrow(string id)
    {
        Inbox?.Snooze(id, DateTimeOffset.UtcNow.AddDays(1));
        RefreshInboxState();
    }

    [RelayCommand]
    public void RestoreInboxItem(string id)
    {
        Inbox?.RestoreDueSnoozes(DateTimeOffset.MaxValue);
        RefreshInboxState();
    }

    [RelayCommand]
    public void AddCurrentTrackToInbox()
    {
        var track = Playback?.State.Track;
        if (track != null && Inbox != null)
        {
            Inbox.Add(track, InboxSource.Manual);
            StatusMessage = L10n.Tr("Added to Inbox", "已添加到收件箱");
            RefreshInboxState();
        }
    }

    public IReadOnlyList<string> MoodChips { get; } =
    [
        L10n.Tr("Podcasts", "播客"),
        L10n.Tr("Energize", "提神"),
        L10n.Tr("Feel Good", "愉悦"),
        L10n.Tr("Workout", "健身"),
        L10n.Tr("Relax", "放松"),
        L10n.Tr("Party", "派对"),
        L10n.Tr("Commute", "通勤"),
        L10n.Tr("Focus", "专注"),
        L10n.Tr("Romance", "浪漫"),
        L10n.Tr("Sad", "伤感"),
        L10n.Tr("Sleep", "睡眠")
    ];

    [RelayCommand]
    private void Select(SidebarSection section)
    {
        if (section == SidebarSection.Search)
        {
            OpenSearchWindow();
            return;
        }

        SelectedSection = section;
        SelectedPlaylist = null;
        SelectedYouTubeImport = null;
        SelectedRelease = null;
        SelectedArtist = null;
    }

    public void RefreshSidebar()
    {
        if (PlaylistService is not null)
        {
            PinnedPlaylists = PlaylistService.PinnedPlaylists();
        }
    }

    public void SelectPlaylist(PlaylistEntity playlist)
    {
        SelectedSection = SidebarSection.Playlists;
        SelectedPlaylist = playlist;
        SelectedYouTubeImport = null;
    }

    public void SelectYouTubeImport(YouTubeImportEntity import)
    {
        SelectedSection = SidebarSection.Playlists;
        SelectedYouTubeImport = import;
        SelectedPlaylist = null;
    }

    [RelayCommand]
    public void ClosePlaylistDetail()
    {
        SelectedPlaylist = null;
        SelectedYouTubeImport = null;
    }

    public void SetUndoableDeletion(PlaylistDeletionSnapshot snapshot)
    {
        UndoablePlaylistDeletion = snapshot;
    }

    public void UndoPlaylistDeletion()
    {
        if (UndoablePlaylistDeletion is not null && PlaylistService is not null)
        {
            PlaylistService.Restore(UndoablePlaylistDeletion);
            UndoablePlaylistDeletion = null;
            RefreshSidebar();
            PlaylistsChanged?.Invoke();
        }
    }

    [RelayCommand]
    public void OpenAddChoice() => ShowAddChoiceSheet = true;

    [RelayCommand]
    public void CloseAddChoice() => ShowAddChoiceSheet = false;

    [RelayCommand]
    public void OpenNewPlaylistDialog()
    {
        ShowAddChoiceSheet = false;
        NewPlaylistName = "";
        ShowNewPlaylistDialog = true;
    }

    [RelayCommand]
    public void CloseNewPlaylistDialog()
    {
        ShowNewPlaylistDialog = false;
        NewPlaylistName = "";
    }

    [RelayCommand]
    public void CreatePlaylist()
    {
        var trimmed = NewPlaylistName.Trim();
        if (string.IsNullOrEmpty(trimmed) || PlaylistService is null) return;
        PlaylistService.Create(trimmed);
        RefreshSidebar();
        CloseNewPlaylistDialog();
        PlaylistsChanged?.Invoke();
    }

    [RelayCommand]
    public void OpenImportDialog()
    {
        ShowAddChoiceSheet = false;
        ImportPlaylistUrl = "";
        ImportError = null;
        ImportBusy = false;
        ShowImportDialog = true;
    }

    [RelayCommand]
    public void CloseImportDialog()
    {
        ShowImportDialog = false;
        ImportPlaylistUrl = "";
        ImportError = null;
        ImportBusy = false;
    }

    [RelayCommand]
    public async Task ImportPlaylistAsync()
    {
        if (YouTubeImportService is null || ImportBusy) return;
        var url = ImportPlaylistUrl.Trim();
        if (string.IsNullOrEmpty(url)) return;

        ImportBusy = true;
        ImportError = null;
        try
        {
            await YouTubeImportService.ImportPlaylistAsync(url).ConfigureAwait(true);
            CloseImportDialog();
            RefreshSidebar();
            PlaylistsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ImportError = ex.Message;
        }
        finally
        {
            ImportBusy = false;
        }
    }

    public event Action? PlaylistsChanged;

    [RelayCommand]
    private void ToggleSidebar()
    {
        SidebarCollapsed = !SidebarCollapsed;
        Preferences.SetBool(PrefKey.SidebarCollapsed, SidebarCollapsed);
    }


    public void RefreshWebHomeStatus()
    {
        if (WebHome is null)
        {
            WebHomeStatusText = L10n.Tr("Web Home helper is not configured.", "Web Home 助手未配置。");
            return;
        }
        WebHomeEnabled = WebHome.IsEnabled;
        WebHomeStatusText = WebHome.Status switch
        {
            Muses.Core.Advanced.WebHomeSessionStatus.Available => L10n.Tr("Web Home session available.", "Web Home 会话可用。"),
            Muses.Core.Advanced.WebHomeSessionStatus.AccountMismatch => L10n.Tr("Web Home account mismatch.", "Web Home 账号不匹配。"),
            Muses.Core.Advanced.WebHomeSessionStatus.PendingConsent => L10n.Tr("Consent required before enabling Web Home.", "启用 Web Home 前需要同意。"),
            Muses.Core.Advanced.WebHomeSessionStatus.Checking => L10n.Tr("Checking Web Home session…", "正在检查 Web Home 会话…"),
            Muses.Core.Advanced.WebHomeSessionStatus.Unavailable => L10n.Tr("Web Home unavailable.", "Web Home 不可用。"),
            Muses.Core.Advanced.WebHomeSessionStatus.Closed => WebHome.IsEnabled
                ? L10n.Tr("Web Home enabled — probe when signed in.", "Web Home 已启用 — 登录后可探测。")
                : L10n.Tr("Web Home is off (default).", "Web Home 默认关闭。"),
            _ => WebHome.Status.ToString()
        };
        OnPropertyChanged(nameof(WebHomeStatusText));
    }

    [RelayCommand]
    public async Task ToggleWebHomeAsync()
    {
        if (WebHome is null) return;
        if (WebHome.IsEnabled)
        {
            await WebHome.DisableAndClearAsync();
        }
        else
        {
            WebHome.EnableWithConsent();
            if (IsAccountSignedIn)
                await WebHome.ProbeSessionAsync();
        }
        WebHomeEnabled = WebHome.IsEnabled;
        RefreshWebHomeStatus();
    }

    [RelayCommand]
    public async Task ProbeWebHomeAsync()
    {
        if (WebHome is null) return;
        if (!WebHome.IsEnabled || !WebHome.HasConsent)
            WebHome.EnableWithConsent();
        await WebHome.ProbeSessionAsync();
        RefreshWebHomeStatus();
    }

    private void NotifyAccountChanged()
    {
        OnPropertyChanged(nameof(ShowGuestBanner));
        OnPropertyChanged(nameof(AccountDisplayName));
        OnPropertyChanged(nameof(AccountErrorMessage));
        OnPropertyChanged(nameof(IsAccountError));
        OnPropertyChanged(nameof(IsAccountSignedIn));
        OnPropertyChanged(nameof(IsAccountSigningIn));
    }

    private void NotifyL10nLabels()
    {
        OnPropertyChanged(nameof(SearchLabel));
        OnPropertyChanged(nameof(HomeLabel));
        OnPropertyChanged(nameof(DiscoverLabel));
        OnPropertyChanged(nameof(LibraryLabel));
        OnPropertyChanged(nameof(RecentlyLabel));
        OnPropertyChanged(nameof(SongsLabel));
        OnPropertyChanged(nameof(AlbumsLabel));
        OnPropertyChanged(nameof(ArtistsLabel));
        OnPropertyChanged(nameof(HistoryLabel));
        OnPropertyChanged(nameof(PlaylistsLabel));
        OnPropertyChanged(nameof(SettingsLabel));
        OnPropertyChanged(nameof(SignInLabel));
        OnPropertyChanged(nameof(GuestBannerTitle));
        OnPropertyChanged(nameof(GuestBannerBody));
        OnPropertyChanged(nameof(NotPlayingLabel));
        OnPropertyChanged(nameof(SettingsGeneralTitle));
        OnPropertyChanged(nameof(SettingsLanguageLabel));
        OnPropertyChanged(nameof(SettingsCloseToTrayLabel));
        OnPropertyChanged(nameof(SettingsTrayLabel));
        OnPropertyChanged(nameof(SettingsPlaybackTitle));
        OnPropertyChanged(nameof(SettingsResumeAfterVideoLabel));
        OnPropertyChanged(nameof(SettingsReplayGainLabel));
        OnPropertyChanged(nameof(SettingsYouTubeTitle));
        OnPropertyChanged(nameof(SettingsAudioQualityTitle));
        OnPropertyChanged(nameof(SettingsPreferredStreamLabel));
        OnPropertyChanged(nameof(SettingsAppearanceTitle));
        OnPropertyChanged(nameof(SettingsLyricsTitle));
        OnPropertyChanged(nameof(SettingsDesktopTitle));
        OnPropertyChanged(nameof(SettingsMiniPlayerLabel));
        OnPropertyChanged(nameof(SettingsDesktopLyricsLabel));
        OnPropertyChanged(nameof(SettingsGlobalHotkeysLabel));
        OnPropertyChanged(nameof(SettingsCatGeneral));
        OnPropertyChanged(nameof(SettingsCatPlayback));
        OnPropertyChanged(nameof(SettingsCatQuality));
        OnPropertyChanged(nameof(SettingsCatAppearance));
        OnPropertyChanged(nameof(SettingsCatYouTube));
        OnPropertyChanged(nameof(SettingsCatLyrics));
        OnPropertyChanged(nameof(SettingsCatDesktop));
        OnPropertyChanged(nameof(SettingsCatAbout));
        OnPropertyChanged(nameof(VideoEmbedUnavailableMessage));
    }

    [RelayCommand]
    private async Task SignInWithGoogleAsync()
    {
        if (Account is null) return;
        await Account.StartGoogleSignInAsync();
        NotifyAccountChanged();
    }

    [RelayCommand]
    private void SignOutYouTube()
    {
        Account?.SignOut();
        NotifyAccountChanged();
    }

    [RelayCommand]
    private void OpenSettings() => SettingsOpen = true;

    [RelayCommand]
    private void OpenPaste()
    {
        PasteError = null;
        PasteOpen = true;
    }

    [RelayCommand]
    private void ClosePaste()
    {
        PasteOpen = false;
        PasteBusy = false;
        PasteError = null;
    }

    [RelayCommand]
    private void TogglePlayback() => Commands.Execute(CommandRegistry.TogglePlayback);

    [RelayCommand]
    private void Next() => Commands.Execute(CommandRegistry.Next);

    [RelayCommand]
    private void Previous() => Commands.Execute(CommandRegistry.Previous);

    [RelayCommand]
    private void ToggleShuffle()
    {
        Playback?.ToggleShuffle();
        RefreshPlayback();
    }

    [RelayCommand]
    private void CycleRepeat()
    {
        Playback?.CycleRepeat();
        RefreshPlayback();
    }

    [RelayCommand]
    private async Task ImportPasteAsync()
    {
        if (Playback is null || _library is null || _ytdlp is null) return;
        PasteBusy = true;
        PasteError = null;
        try
        {
            await PlayUrlAsync(PasteUrl).ConfigureAwait(true);
            PasteOpen = false;
            PasteUrl = "";
        }
        catch (Exception ex)
        {
            PasteError = ex.Message;
        }
        finally
        {
            PasteBusy = false;
            RefreshPlayback();
        }
    }

    public async Task PlayUrlAsync(string raw)
    {
        if (Playback is null || _library is null || _ytdlp is null)
            throw new InvalidOperationException("Playback is not attached.");
        var kind = YouTubeLinks.Detect(raw);
        if (kind == YouTubeLinkKind.Unknown)
            throw new InvalidOperationException(L10n.Tr("Not a YouTube link.", "不是 YouTube 链接。"));

        if (kind == YouTubeLinkKind.Playlist)
        {
            var entries = await _ytdlp.FetchPlaylistAsync(raw, TimeSpan.FromSeconds(40)).ConfigureAwait(true);
            if (entries.Count == 0)
                throw new InvalidOperationException(L10n.Tr("Playlist is empty.", "歌单为空。"));
            var snaps = entries.Select(ToSnapshot).ToList();
            foreach (var snap in snaps)
                _library.UpsertFromYouTube(snap.YouTubeId, snap.Title, snap.Artist, snap.DurationSeconds, snap.ArtworkUrl);
            StatusMessage = L10n.Tr($"Playing {snaps.Count} tracks", $"正在播放 {snaps.Count} 首");
            Playback.PlayTrack(snaps[0], snaps, QueueSource.Import);
            return;
        }

        var videoId = YouTubeLinks.ExtractVideoId(raw)
                      ?? throw new InvalidOperationException(L10n.Tr("Could not read video id.", "无法解析视频 ID。"));
        var meta = await _ytdlp.FetchVideoMetadataAsync(videoId, TimeSpan.FromSeconds(25)).ConfigureAwait(true);
        var title = meta?.Title ?? videoId;
        var artist = meta?.Uploader ?? "";
        var duration = meta?.Duration ?? 0;
        var snapOne = _library.UpsertFromYouTube(videoId, title, artist, duration, YouTubeLinks.ThumbnailUrl(videoId));
        StatusMessage = title;
        Playback.PlayTrack(snapOne, [snapOne], QueueSource.Search);
    }

    private static TrackSnapshot ToSnapshot(YTDlpPlaylistEntry entry) => new(
        Guid.NewGuid(),
        entry.Title,
        entry.Uploader ?? "",
        entry.Album,
        entry.Duration ?? 0,
        entry.Id,
        YouTubeLinks.ThumbnailUrl(entry.Id));

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        UpdateChecker ??= new GitHubUpdateChecker(currentVersion: "0.1.0");

        IsCheckingForUpdates = true;
        UpdateStatusMessage = L10n.Tr("Checking for updates...", "正在检查更新...");

        try
        {
            var info = await UpdateChecker.CheckForUpdatesAsync().ConfigureAwait(true);
            AvailableUpdateInfo = info;
            HasAvailableUpdate = info.HasUpdate;

            if (info.HasUpdate)
            {
                UpdateStatusMessage = string.Format(
                    L10n.Tr("Version {0} is available! (Current: {1})", "发现新版本 {0}！（当前版本：{1}）"),
                    info.LatestVersion, info.CurrentVersion);
            }
            else
            {
                UpdateStatusMessage = string.Format(
                    L10n.Tr("You're up to date (v{0})", "已是最新版本 (v{0})"),
                    info.CurrentVersion);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = string.Format(L10n.Tr("Check failed: {0}", "检查更新失败: {0}"), ex.Message);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public void DownloadUpdate()
    {
        if (AvailableUpdateInfo != null && !string.IsNullOrWhiteSpace(AvailableUpdateInfo.DownloadUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AvailableUpdateInfo.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
