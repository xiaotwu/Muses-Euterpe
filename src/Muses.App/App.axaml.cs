using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Muses.App.Services;
using Muses.App.Theme;
using Muses.App.ViewModels;
using Muses.App.Views;
using Muses.Core.Library;
using Muses.Core.L10n;
using Muses.Core.Playback;
using Muses.Core.Podcasts;
using Muses.Core.Preferences;
using Muses.Infrastructure;
using Muses.Core.Queue;
using Muses.Infrastructure.Account;
using Muses.Infrastructure.Discovery;
using Muses.Infrastructure.Playback;
using Muses.Infrastructure.Platform;
using Muses.Infrastructure.YTDlp;
using Muses.Persistence;
using Muses.Platform.Windows;

namespace Muses.App;

public partial class App : Application
{
    private SqliteStore? _store;
    private ProcessStreamEngine? _engine;
    private ShellViewModel? _shell;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        GlassChrome.Apply(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _store = SqliteStore.Open();
            var preferences = new SqlitePreferences(_store);
            MotionChrome.Apply(this);
            ApplyTheme(preferences);
            ApplyLanguage(preferences);

            var queue = new QueueService { Store = _store };
            queue.Restore();
            var library = new LibraryService(_store);
            var ytdlpRunner = new YTDlpRunner();
            var ytdlp = new YTDlpBridge(runner: ytdlpRunner);
            var quality = ProcessStreamEngine.MapAudioQualityPref(preferences.GetString(PrefKey.AudioQuality, "best"));
            _engine = new ProcessStreamEngine(ytdlp, quality);
            _engine.SetReplayGainEnabled(preferences.GetBool(PrefKey.ReplayGainEnabled, true));
            _engine.SetCrossfadeSeconds(preferences.GetDouble(PrefKey.CrossfadeSeconds, 0));
            var podcastProgress = new PodcastProgressService(_store);
            var playback = new PlaybackService(_engine, queue, library, preferences, podcastProgress: podcastProgress);
            var playlistService = new PlaylistService(_store);
            var youtubeImportService = new YouTubeImportService(_store, _store, ytdlp);
            var catalog = new Muses.Infrastructure.Catalog.YouTubeCatalogService(_store, _store, ytdlp);
            catalog.RebuildFromTrackMetadata();
            var situational = new Muses.Core.Recommendation.SituationalRecommendationService(library);
            var search = new Muses.Infrastructure.Search.GlobalSearchService(library, catalog, ytdlp);
            var lyrics = new Muses.Infrastructure.Lyrics.LyricsService(new Muses.Infrastructure.Lyrics.LrclibLyricsProvider(), _store);
            var history = new Muses.Infrastructure.History.HistoryService(_store, library, playback);
            var eq = new Muses.Infrastructure.Advanced.EQService(_store);
            void PushEq(System.Collections.Generic.IReadOnlyList<Muses.Core.Advanced.EQBand> bands) =>
                playback.SetEq(Muses.Infrastructure.Advanced.EQService.ToEngineBands(bands));
            eq.BandsChanged += PushEq;
            PushEq(eq.ActiveBands);
            var focus = new Muses.Infrastructure.Advanced.FocusService(_store, playback);
            var notes = new Muses.Infrastructure.Advanced.NotesService(_store);
            var inbox = new Muses.Infrastructure.Advanced.InboxService(_store, playback);
            var automation = new Muses.Infrastructure.Advanced.AutomationService(_store, playback, actionHandler: (act, snap) =>
            {
                switch (act)
                {
                    case Muses.Core.Advanced.AutomationAction.LikeTrack:
                        library.ToggleLike(snap.Id);
                        break;
                    case Muses.Core.Advanced.AutomationAction.AddToInbox:
                        inbox.Add(snap, Muses.Core.Advanced.InboxSource.Automation);
                        break;
                    case Muses.Core.Advanced.AutomationAction.PlayNext:
                        playback.Queue.PlayNext(snap);
                        break;
                    case Muses.Core.Advanced.AutomationAction.AddToQueue:
                        playback.Queue.AddToQueue(snap);
                        break;
                }
            });

            // Account must never block playback composition.
            var isolatedDataRoot = Environment.GetEnvironmentVariable("MUSES_DATA_HOME");
            var isIsolatedProfile = Path.IsPathFullyQualified(isolatedDataRoot ?? "");
            var credentialStore = isIsolatedProfile
                ? new MemoryCredentialStore()
                : PlatformCredentialStore.CreateDefault();
            var account = new YouTubeAccountService(credentialStore);
            var webHomeClient = new Muses.Infrastructure.Advanced.ProcessWebHomeHelperClient(
                helperPath: isIsolatedProfile ? "" : null);
            var webHome = new Muses.Infrastructure.Advanced.WebHomeSessionController(
                webHomeClient,
                () => account.IsSignedIn ? account.Profile?.ChannelId : null,
                preferences);

            var hiddenToTray = false;
            void ShowMainWindow()
            {
                if (desktop.MainWindow is not { } window)
                    return;

                hiddenToTray = false;
                window.Show();
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
            }

            void Quit() => desktop.Shutdown();

            var mediaControls = new WindowsSMTCService(playback, dispatch: action => Dispatcher.UIThread.Post(action));
            var globalHotkeys = new WindowsGlobalHotkeys();
            var audioProbe = new WindowsAudioOutputProbe();
            var tray = new WindowsTrayController(playback, preferences, ShowMainWindow, Quit);

            var shell = new ShellViewModel
            {
                SettingsDataFolderPath = Path.GetDirectoryName(_store.DatabasePath) ?? LocalDataMaintenance.AppDataRoot(),
                PolyhymniaLibraryImporter = _store.DatabasePath is { } libraryPath ? new PolyhymniaLibraryImporter(libraryPath) : null,
                Podcasts = new PodcastsViewModel(_store, new YouTubePodcastCatalog(), library, playback, podcastProgress)
            };
            shell.Attach(
                playback, library, ytdlp, playlistService, youtubeImportService, catalog,
                situational, search, lyrics, history, eq, focus, notes, inbox, automation,
                preferences, account, mediaControls, tray, webHome, _engine, globalHotkeys, audioProbe);
            _shell = shell;
            shell.RequestAppExit += Quit;

            var main = new MainWindow { DataContext = shell };
            var packagePlaybackSmoke = PackagePlaybackSmoke.Parse(desktop.Args);
            desktop.MainWindow = main;
            main.Opened += RestorePlaybackOnOpen;
            async void RestorePlaybackOnOpen(object? sender, EventArgs e)
            {
                main.Opened -= RestorePlaybackOnOpen;
                mediaControls.Start(main.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                if (packagePlaybackSmoke is not null)
                    desktop.Shutdown(await packagePlaybackSmoke.RunAsync(playback, library));
                else
                    await playback.RestoreCurrentPausedAsync();
            }

            var checkpointTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            checkpointTimer.Tick += (_, _) =>
            {
                if (playback.State.IsPlaying)
                    playback.CheckpointPosition();
                podcastProgress.RetryPending();
            };
            checkpointTimer.Start();

            var trayHost = new TrayIconHost(this, tray);
            void RestoreIfTrayDisappears()
            {
                if (hiddenToTray && !trayHost.CanRestoreWindow)
                    ShowMainWindow();
            }

            trayHost.AvailabilityChanged += RestoreIfTrayDisappears;
            main.Closing += (_, e) =>
            {
                if (e.CloseReason != WindowCloseReason.WindowClosing || e.IsProgrammatic
                    || !shell.CloseToTray || !trayHost.CanRestoreWindow)
                    return;

                e.Cancel = true;
                hiddenToTray = true;
                if (shell.IsVideoOverlayOpen)
                    shell.CloseVideoOverlay();
                main.Hide();
            };
            if (tray.ShouldEnableFromPrefs())
                tray.SetEnabled(true);

            desktop.Exit += (_, _) =>
            {
                checkpointTimer.Stop();
                main.Opened -= RestorePlaybackOnOpen;
                shell.Podcasts?.Stop();
                shell.StopPlaybackUpdates();
                playback.StopForShutdown();
                playback.CheckpointPosition();
                shell.StopAccountHome();
                webHomeClient.Dispose();
                focus.Dispose();
                inbox.Dispose();
                automation.Dispose();
                queue.Persist();
                trayHost.AvailabilityChanged -= RestoreIfTrayDisappears;
                trayHost.Dispose();
                mediaControls.Dispose();
                globalHotkeys.Dispose();
                _engine.Dispose();
                ytdlpRunner.Dispose();
                var reset = _shell?.ResetOnExit == true;
                _store.Dispose();
                if (reset)
                    LocalDataMaintenance.ResetDataFiles();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(IPreferences preferences)
    {
        var theme = AppThemeCodec.Parse(preferences.GetString(PrefKey.Theme, "system"));
        if (Current is null) return;
        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.System => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };
    }

    private static void ApplyLanguage(IPreferences preferences)
    {
        var lang = preferences.GetString(PrefKey.Language, "system");
        if (L10n.Source is SystemLanguageSource sys)
            sys.Preference = lang;
    }
}
