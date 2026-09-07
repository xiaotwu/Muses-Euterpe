using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Catalog;
using Muses.Core.Domain;
using Muses.Core.Preferences;
using Muses.Core.Queue;
using Muses.Core.Search;
using Muses.Infrastructure.Search;
using Muses.Infrastructure.YTDlp;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class SearchWindow : Window
{
    private static SearchWindow? _activeInstance;

    private readonly ShellViewModel _vm;
    private readonly GlobalSearchService _search;

    public static void ShowOrActivate(Window owner, ShellViewModel vm, string? initialQuery = null, GlobalSearchScope? initialScope = null)
    {
        if (_activeInstance is not null)
        {
            if (initialScope is not null) _activeInstance.SetScope(initialScope.Value);
            if (initialQuery is not null) _activeInstance.SetQuery(initialQuery);
            _activeInstance.Activate();
            _activeInstance.SearchBox.SelectAll();
            _activeInstance.SearchBox.Focus();
            return;
        }

        if (vm.GlobalSearch is null) return;
        var win = new SearchWindow(vm, vm.GlobalSearch);
        _activeInstance = win;

        if (initialScope is not null) win.SetScope(initialScope.Value);
        if (initialQuery is not null) win.SetQuery(initialQuery);

        win.Closed += (_, _) => _activeInstance = null;
        win.Show();
        win.SearchBox.SelectAll();
        win.SearchBox.Focus();
    }

    public SearchWindow() : this(new ShellViewModel(), new GlobalSearchService(new Core.Library.LibraryService(Muses.Persistence.SqliteStore.Open(inMemory: true))))
    {
    }

    public SearchWindow(ShellViewModel vm, GlobalSearchService search)
    {
        _vm = vm;
        _search = search;
        InitializeComponent();

        _search.Changed += OnSearchChanged;
        KeyDown += OnWindowKeyDown;
        Loaded += (_, _) => SearchBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _search.Changed -= OnSearchChanged;
        _search.Reset();
    }

    public void SetQuery(string query)
    {
        SearchBox.Text = query;
    }

    public void SetScope(GlobalSearchScope scope)
    {
        _search.Scope = scope;
        UpdateScopeButtons();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PlayTopResult();
            e.Handled = true;
        }
    }

    private void PlayTopResult()
    {
        if (_search.TrackResults.Count > 0)
        {
            var top = _search.TrackResults[0];
            _vm.Playback?.PlayTrack(top, _search.TrackResults, QueueSource.Search);
            Close();
            return;
        }

        if (_search.YouTubeResults.Count > 0)
        {
            var top = _search.YouTubeResults[0];
            PlayYouTubeEntry(top);
            Close();
            return;
        }

        if (_search.ReleaseResults.Count > 0)
        {
            _vm.SelectRelease(_search.ReleaseResults[0]);
            Close();
            return;
        }

        if (_search.CatalogArtistResults.Count > 0)
        {
            _vm.SelectArtist(_search.CatalogArtistResults[0]);
            Close();
            return;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text ?? "";
        ClearBtn.IsVisible = !string.IsNullOrEmpty(text);
        _search.Query = text;
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void OnScopeAll(object? sender, RoutedEventArgs e) => SetScope(GlobalSearchScope.All);
    private void OnScopeLibrary(object? sender, RoutedEventArgs e) => SetScope(GlobalSearchScope.Library);
    private void OnScopeYouTube(object? sender, RoutedEventArgs e) => SetScope(GlobalSearchScope.YouTube);

    private void UpdateScopeButtons()
    {
        ScopeAllBtn.Classes.Set("selected", _search.Scope == GlobalSearchScope.All);
        ScopeLibraryBtn.Classes.Set("selected", _search.Scope == GlobalSearchScope.Library);
        ScopeYouTubeBtn.Classes.Set("selected", _search.Scope == GlobalSearchScope.YouTube);
    }

    private void OnSearchChanged()
    {
        Dispatcher.UIThread.Post(RenderSearchResults);
    }

    private void RenderSearchResults()
    {
        SearchSpinner.IsVisible = _search.IsSearchingYouTube;

        var hasQuery = !string.IsNullOrWhiteSpace(_search.Query);
        LandingPanel.IsVisible = !hasQuery;
        ResultsPanel.IsVisible = hasQuery;

        if (!hasQuery) return;

        // 1. Songs Group
        SongsResultsContainer.Children.Clear();
        foreach (var track in _search.TrackResults.Take(8))
        {
            SongsResultsContainer.Children.Add(BuildSongRow(track, _search.TrackResults));
        }
        SongsGroup.IsVisible = _search.TrackResults.Count > 0;

        // 2. Albums Group
        AlbumsResultsContainer.Children.Clear();
        foreach (var rel in _search.ReleaseResults.Take(10))
        {
            AlbumsResultsContainer.Children.Add(BuildReleaseCard(rel));
        }
        AlbumsGroup.IsVisible = _search.ReleaseResults.Count > 0;

        // 3. Artists Group
        ArtistsResultsContainer.Children.Clear();
        foreach (var artist in _search.CatalogArtistResults.Take(10))
        {
            ArtistsResultsContainer.Children.Add(BuildArtistCard(artist));
        }
        ArtistsGroup.IsVisible = _search.CatalogArtistResults.Count > 0;

        // 4. YouTube Music Group
        YouTubeResultsContainer.Children.Clear();
        foreach (var yt in _search.YouTubeResults.Take(12))
        {
            YouTubeResultsContainer.Children.Add(BuildYouTubeRow(yt));
        }
        YouTubeGroup.IsVisible = _search.YouTubeResults.Count > 0;

        NoResultsPanel.IsVisible = !_search.HasResults && !_search.IsSearchingYouTube;
    }

    private Button BuildSongRow(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context)
    {
        var btn = new Button { Classes = { "searchResultRow" }, Tag = track };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var art = new ArtworkControl
        {
            Width = 38, Height = 38,
            CornerRadius = new Avalonia.CornerRadius(5),
            GlyphSize = 16,
            SourceUrl = track.ArtworkUrl
        };
        Grid.SetColumn(art, 0);
        grid.Children.Add(art);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Margin = new Avalonia.Thickness(10, 0, 8, 0)
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = track.Title, FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = track.Artist, FontSize = 11,
            Foreground = ThemeBrushes.TextSecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var playIcon = new PathIcon
        {
            Data = StreamGeometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z"),
            Width = 12, Height = 12, Foreground = ThemeBrushes.TextSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(playIcon, 2);
        grid.Children.Add(playIcon);

        btn.Content = grid;
        btn.Click += (_, _) =>
        {
            _vm.Playback?.PlayTrack(track, context, QueueSource.Search);
            Close();
        };
        return btn;
    }

    private Button BuildReleaseCard(CatalogReleaseProjection release)
    {
        var btn = new Button
        {
            Classes = { "searchResultRow" },
            Width = 130, Height = 175,
            Tag = release
        };

        var stack = new StackPanel { Spacing = 6 };
        var art = new ArtworkControl
        {
            Width = 130, Height = 130,
            CornerRadius = new Avalonia.CornerRadius(8),
            GlyphSize = 28,
            SourceUrl = release.ArtworkUrl
        };
        stack.Children.Add(art);

        stack.Children.Add(new TextBlock
        {
            Text = release.Title, FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = release.ArtistName, FontSize = 10,
            Foreground = ThemeBrushes.TextSecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
        });

        btn.Content = stack;
        btn.Click += (_, _) =>
        {
            _vm.SelectRelease(release);
            Close();
        };
        return btn;
    }

    private Button BuildArtistCard(CatalogArtistProjection artist)
    {
        var btn = new Button
        {
            Classes = { "searchResultRow" },
            Width = 130, Height = 175,
            Tag = artist
        };

        var stack = new StackPanel { Spacing = 6 };
        var art = new ArtworkControl
        {
            Width = 130, Height = 130,
            CornerRadius = new Avalonia.CornerRadius(65),
            GlyphSize = 28,
            SourceUrl = artist.ArtworkUrl ?? artist.Tracks.FirstOrDefault()?.ArtworkUrl
        };
        stack.Children.Add(art);

        stack.Children.Add(new TextBlock
        {
            Text = artist.Name, FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush, HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{artist.Tracks.Count} songs", FontSize = 10,
            Foreground = ThemeBrushes.TextSecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center
        });

        btn.Content = stack;
        btn.Click += (_, _) =>
        {
            _vm.SelectArtist(artist);
            Close();
        };
        return btn;
    }

    private Button BuildYouTubeRow(YTDlpPlaylistEntry entry)
    {
        var btn = new Button { Classes = { "searchResultRow" }, Tag = entry };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var art = new ArtworkControl
        {
            Width = 38, Height = 38,
            CornerRadius = new Avalonia.CornerRadius(5),
            GlyphSize = 16,
            SourceUrl = $"https://i.ytimg.com/vi/{entry.Id}/hqdefault.jpg"
        };
        Grid.SetColumn(art, 0);
        grid.Children.Add(art);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Margin = new Avalonia.Thickness(10, 0, 8, 0)
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = entry.Title, FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = entry.Uploader ?? "YouTube Music", FontSize = 11,
            Foreground = ThemeBrushes.TextSecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var ytMark = new YouTubeMark { MarkSize = 14, Margin = new Avalonia.Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(ytMark, 2);
        grid.Children.Add(ytMark);

        btn.Content = grid;
        btn.Click += (_, _) =>
        {
            PlayYouTubeEntry(entry);
            Close();
        };
        return btn;
    }

    private void PlayYouTubeEntry(YTDlpPlaylistEntry entry)
    {
        if (_vm.Library is null || _vm.Playback is null) return;
        var snap = _vm.Library.UpsertFromYouTube(
            entry.Id,
            entry.Title,
            entry.Uploader ?? "YouTube Music",
            entry.Duration ?? 0,
            $"https://i.ytimg.com/vi/{entry.Id}/hqdefault.jpg");
        _vm.Playback.PlayTrack(snap, [snap], QueueSource.Search);
    }

    private void OnBrowseSongs(object? sender, RoutedEventArgs e)
    {
        _vm.SelectCommand.Execute(SidebarSection.Songs);
        Close();
    }

    private void OnBrowseAlbums(object? sender, RoutedEventArgs e)
    {
        _vm.SelectCommand.Execute(SidebarSection.Albums);
        Close();
    }

    private void OnBrowseArtists(object? sender, RoutedEventArgs e)
    {
        _vm.SelectCommand.Execute(SidebarSection.Artists);
        Close();
    }
}
