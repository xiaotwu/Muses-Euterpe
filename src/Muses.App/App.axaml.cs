using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using Muses.App.ViewModels;
using Muses.App.Views;
using Muses.Core.Library;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Infrastructure.Playback;
using Muses.Infrastructure.YTDlp;
using Muses.Persistence;
using Muses.App.Theme;

namespace Muses.App;

public partial class App : Application
{
    private SqliteStore? _store;
    private ProcessStreamEngine? _engine;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (Current?.Styles[0] is FluentAvaloniaTheme fa)
            fa.CustomAccentColor = ThemeBrushes.AccentColor;

        RequestedThemeVariant = ThemeVariant.Dark;

        // Sync MusesMotion* duration resources (and zero them under OS reduce-motion) before chrome loads.
        MotionChrome.Apply(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _store = SqliteStore.Open();
            var queue = new QueueService { Store = _store };
            queue.Restore();
            var library = new LibraryService(_store);
            var ytdlp = new YTDlpBridge();
            _engine = new ProcessStreamEngine(ytdlp);
            var playback = new PlaybackService(_engine, queue, library);
            var playlistService = new PlaylistService(_store);
            var youtubeImportService = new YouTubeImportService(_store, _store, ytdlp);
            var catalog = new Muses.Infrastructure.Catalog.YouTubeCatalogService(_store, _store, ytdlp);
            catalog.RebuildFromTrackMetadata();
            var homeFeedCache = new Muses.Infrastructure.Discovery.HomeFeedCache();
            var discoveryProvider = new Muses.Infrastructure.Discovery.YTDlpDiscoveryProvider(ytdlp);
            var homeDiscovery = new Muses.Infrastructure.Discovery.HomeDiscoveryService(discoveryProvider, homeFeedCache, library);
            var situational = new Muses.Core.Recommendation.SituationalRecommendationService(library);
            var search = new Muses.Infrastructure.Search.GlobalSearchService(library, catalog, ytdlp);
            var lyrics = new Muses.Infrastructure.Lyrics.LyricsService(new Muses.Infrastructure.Lyrics.LrclibLyricsProvider(), _store);
            var history = new Muses.Infrastructure.History.HistoryService(_store, library, playback);
            var eq = new Muses.Infrastructure.Advanced.EQService(_store);
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

            var shell = new ShellViewModel();
            shell.Attach(playback, library, ytdlp, playlistService, youtubeImportService, homeDiscovery, catalog, situational, search, lyrics, history, eq, focus, notes, inbox, automation);
            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.Exit += (_, _) =>
            {
                focus.Dispose();
                inbox.Dispose();
                automation.Dispose();
                queue.Persist();
                _engine.Dispose();
                _store.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
