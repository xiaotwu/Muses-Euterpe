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

public partial class CatalogArtistsView : UserControl
{
    private ShellViewModel? _vm;

    public CatalogArtistsView()
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

        var artists = _vm.Catalog.Artists();
        CountLabel.Text = $"{artists.Count} artists";
        ArtistsWrapPanel.Children.Clear();

        foreach (var artist in artists)
        {
            var card = BuildArtistCard(artist);
            ArtistsWrapPanel.Children.Add(card);
        }
    }

    private Control BuildArtistCard(CatalogArtistProjection artist)
    {
        var card = new ArtistCardControl
        {
            ArtistName = artist.Name,
            ArtistDetail = $"{artist.Tracks.Count} songs",
            ArtworkUrl = artist.ArtworkUrl ?? artist.Tracks.FirstOrDefault()?.ArtworkUrl,
            CardWidth = 200,
            CardHeight = 264,
            Margin = new Avalonia.Thickness(10)
        };

        card.CardSelected += (_, _) =>
        {
            _vm?.SelectArtist(artist);
        };

        card.PlayRequested += (_, _) =>
        {
            if (artist.Tracks.Count > 0 && _vm?.Playback is not null)
            {
                _vm.Playback.PlayTrack(artist.Tracks[0], artist.Tracks, QueueSource.Album);
            }
        };

        return card;
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        _vm?.Catalog?.RebuildFromTrackMetadata();
        UpdateGrid();
    }
}
