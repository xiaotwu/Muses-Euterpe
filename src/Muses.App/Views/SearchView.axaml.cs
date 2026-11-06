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
using Muses.Core.L10n;
using Muses.Core.Preferences;
using Muses.Core.Queue;
using Muses.Core.Search;
using Muses.Infrastructure.Search;
using Muses.Infrastructure.YTDlp;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class SearchView : UserControl
{
    private ShellViewModel? _vm;

    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) => SetViewModel(DataContext as ShellViewModel);
        Unloaded += (_, _) => SetViewModel(null);
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded) SetViewModel(DataContext as ShellViewModel);
        };
    }

    private void SetViewModel(ShellViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;

        if (_vm is not null)
        {
            _vm.SearchFocusRequested -= OnSearchFocusRequested;
            if (_vm.GlobalSearch is { } previousSearch) previousSearch.Changed -= OnSearchChanged;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.SearchFocusRequested += OnSearchFocusRequested;
            if (_vm.GlobalSearch is { } search) search.Changed += OnSearchChanged;
        }
        RenderSearchResults();
    }

    private void OnSearchFocusRequested() => Dispatcher.UIThread.Post(() =>
    {
        if (_vm?.IsSearch != true || _vm.SettingsOpen) return;
        SearchBox.Focus();
        SearchBox.SelectAll();
    });

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
        if (_vm is not { GlobalSearch: { } search } vm) return;

        if (search.TrackResults.Count > 0)
        {
            vm.Playback?.PlayTrack(search.TrackResults[0], search.TrackResults, QueueSource.Search);
            vm.ReturnFromSearch();
            return;
        }

        if (search.YouTubeResults.Count > 0)
        {
            PlayYouTubeEntry(search.YouTubeResults[0]);
            return;
        }

        if (search.ReleaseResults.Count > 0)
        {
            vm.SelectRelease(search.ReleaseResults[0]);
            return;
        }

        if (search.CatalogArtistResults.Count > 0) vm.SelectArtist(search.CatalogArtistResults[0]);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text ?? "";
        ClearBtn.IsVisible = !string.IsNullOrEmpty(text);
        if (_vm?.GlobalSearch is { } search) search.Query = text;
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void OnScopeAll(object? sender, RoutedEventArgs e) => SetScope(GlobalSearchScope.All);
    private void OnScopeLibrary(object? sender, RoutedEventArgs e) => SetScope(GlobalSearchScope.Library);
    private void OnScopeYouTube(object? sender, RoutedEventArgs e) => SetScope(GlobalSearchScope.YouTube);

    private void SetScope(GlobalSearchScope scope)
    {
        if (_vm?.GlobalSearch is { } search) search.Scope = scope;
    }

    private void OnSearchChanged()
    {
        Dispatcher.UIThread.Post(RenderSearchResults);
    }

    private void RenderSearchResults()
    {
        if (_vm?.GlobalSearch is not { } search) return;

        if (SearchBox.Text != search.Query) SearchBox.Text = search.Query;
        ScopeAllBtn.Classes.Set("selected", search.Scope == GlobalSearchScope.All);
        ScopeLibraryBtn.Classes.Set("selected", search.Scope == GlobalSearchScope.Library);
        ScopeYouTubeBtn.Classes.Set("selected", search.Scope == GlobalSearchScope.YouTube);
        SearchSpinner.IsVisible = search.IsSearchingYouTube;

        var hasQuery = !string.IsNullOrWhiteSpace(search.Query);
        LandingPanel.IsVisible = !hasQuery;
        ResultsPanel.IsVisible = hasQuery;

        if (!hasQuery) return;

        // 1. Songs Group
        SongsResultsContainer.Children.Clear();
        var tracks = search.TrackResults;
        foreach (var track in tracks.Take(8))
        {
            SongsResultsContainer.Children.Add(BuildSongRow(track, tracks));
        }
        SongsGroup.IsVisible = tracks.Count > 0;

        // 2. Albums Group
        AlbumsResultsContainer.Children.Clear();
        foreach (var rel in search.ReleaseResults.Take(10))
        {
            AlbumsResultsContainer.Children.Add(BuildReleaseCard(rel));
        }
        AlbumsGroup.IsVisible = search.ReleaseResults.Count > 0;

        // 3. Artists Group
        ArtistsResultsContainer.Children.Clear();
        foreach (var artist in search.CatalogArtistResults.Take(10))
        {
            ArtistsResultsContainer.Children.Add(BuildArtistCard(artist));
        }
        ArtistsGroup.IsVisible = search.CatalogArtistResults.Count > 0;

        // 4. YouTube Music Group
        YouTubeResultsContainer.Children.Clear();
        foreach (var yt in search.YouTubeResults.Take(12))
        {
            YouTubeResultsContainer.Children.Add(BuildYouTubeRow(yt));
        }
        YouTubeGroup.IsVisible = search.YouTubeResults.Count > 0;

        NoResultsPanel.IsVisible = !search.HasResults && !search.IsSearchingYouTube;
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
            _vm?.Playback?.PlayTrack(track, context, QueueSource.Search);
            _vm?.ReturnFromSearch();
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
        btn.Click += (_, _) => _vm?.SelectRelease(release);
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
            Text = L10n.Tr($"{artist.Tracks.Count} songs", $"{artist.Tracks.Count} 首歌曲"), FontSize = 10,
            Foreground = ThemeBrushes.TextSecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center
        });

        btn.Content = stack;
        btn.Click += (_, _) => _vm?.SelectArtist(artist);
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
        btn.Click += (_, _) => PlayYouTubeEntry(entry);
        return btn;
    }

    private void PlayYouTubeEntry(YTDlpPlaylistEntry entry)
    {
        if (_vm is not { Library: { } library, Playback: { } playback } vm) return;
        var snap = library.UpsertFromYouTube(
            entry.Id,
            entry.Title,
            entry.Uploader ?? "YouTube Music",
            entry.Duration ?? 0,
            $"https://i.ytimg.com/vi/{entry.Id}/hqdefault.jpg");
        playback.PlayTrack(snap, [snap], QueueSource.Search);
        vm.ReturnFromSearch();
    }

    private void OnBrowseSongs(object? sender, RoutedEventArgs e)
    {
        _vm?.SelectCommand.Execute(SidebarSection.Songs);
    }

    private void OnBrowseAlbums(object? sender, RoutedEventArgs e)
    {
        _vm?.SelectCommand.Execute(SidebarSection.Albums);
    }

    private void OnBrowseArtists(object? sender, RoutedEventArgs e)
    {
        _vm?.SelectCommand.Execute(SidebarSection.Artists);
    }
}
