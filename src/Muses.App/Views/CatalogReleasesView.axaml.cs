using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Catalog;
using Muses.Core.Domain;
using Muses.Core.Queue;

namespace Muses.App.Views;

public partial class CatalogReleasesView : UserControl
{
    private ShellViewModel? _vm;
    private int _filterMode = 0; // 0: All, 1: Albums, 2: Singles & EPs
    private int _sortMode = 0;   // 0: Title, 1: Artist, 2: Year

    public CatalogReleasesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                UpdateGrid();
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm?.Catalog is not null)
        {
            _vm.Catalog.Changed -= OnCatalogChanged;
        }

        _vm = DataContext as ShellViewModel;
        if (_vm?.Catalog is not null)
        {
            _vm.Catalog.Changed += OnCatalogChanged;
        }
        UpdateGrid();
    }

    private void OnCatalogChanged()
    {
        Dispatcher.UIThread.Post(UpdateGrid);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateGrid();
    }

    private void UpdateGrid()
    {
        if (_vm?.Catalog is null) return;

        var releases = _vm.Catalog.Releases();

        // Filter
        if (_filterMode == 1)
        {
            releases = releases.Where(r => r.Kind == CatalogReleaseKind.Album).ToList();
        }
        else if (_filterMode == 2)
        {
            releases = releases.Where(r => r.Kind == CatalogReleaseKind.Single || r.Kind == CatalogReleaseKind.Ep).ToList();
        }

        // Sort
        releases = _sortMode switch
        {
            1 => releases.OrderBy(r => r.ArtistName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title).ToList(),
            2 => releases.OrderByDescending(r => r.Year ?? 0).ThenBy(r => r.Title).ToList(),
            _ => releases.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList()
        };

        CountLabel.Text = $"{releases.Count} releases";
        ReleasesWrapPanel.Children.Clear();

        foreach (var release in releases)
        {
            var card = BuildReleaseCard(release);
            ReleasesWrapPanel.Children.Add(card);
        }
    }

    private Control BuildReleaseCard(CatalogReleaseProjection release)
    {
        var subText = release.ArtistName;
        if (release.Year is not null) subText += $" · {release.Year}";

        var tag = release.Kind switch
        {
            CatalogReleaseKind.Single => "SINGLE",
            CatalogReleaseKind.Ep => "EP",
            _ => "ALBUM"
        };

        var card = new AlbumCardControl
        {
            CardTitle = release.Title,
            CardSubtitle = subText,
            ArtworkUrl = release.ArtworkUrl,
            TagText = tag,
            CardStyle = AlbumCardPresentationStyle.HeroCard,
            CardWidth = 200,
            CardHeight = 264,
            Margin = new Avalonia.Thickness(10)
        };

        card.CardSelected += (_, _) =>
        {
            _vm?.SelectRelease(release);
        };

        card.PlayRequested += (_, _) =>
        {
            if (release.Tracks.Count > 0 && _vm?.Playback is not null)
            {
                _vm.Playback.PlayTrack(release.Tracks[0], release.Tracks, QueueSource.Album);
            }
        };

        return card;
    }

    private void OnFilterAll(object? sender, RoutedEventArgs e)
    {
        _filterMode = 0;
        FilterAllBtn.Classes.Set("selected", true);
        FilterAlbumsBtn.Classes.Set("selected", false);
        FilterSinglesBtn.Classes.Set("selected", false);
        UpdateGrid();
    }

    private void OnFilterAlbums(object? sender, RoutedEventArgs e)
    {
        _filterMode = 1;
        FilterAllBtn.Classes.Set("selected", false);
        FilterAlbumsBtn.Classes.Set("selected", true);
        FilterSinglesBtn.Classes.Set("selected", false);
        UpdateGrid();
    }

    private void OnFilterSingles(object? sender, RoutedEventArgs e)
    {
        _filterMode = 2;
        FilterAllBtn.Classes.Set("selected", false);
        FilterAlbumsBtn.Classes.Set("selected", false);
        FilterSinglesBtn.Classes.Set("selected", true);
        UpdateGrid();
    }

    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        _sortMode = SortCombo?.SelectedIndex ?? 0;
        UpdateGrid();
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        _vm?.Catalog?.RebuildFromTrackMetadata();
        UpdateGrid();
    }
}
