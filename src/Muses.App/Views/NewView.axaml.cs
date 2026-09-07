using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.Queue;
using Muses.Core.Recommendation;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class NewView : UserControl
{
    private ShellViewModel? _vm;

    public NewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm = DataContext as ShellViewModel;
        UpdateNewView();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateNewView();
    }

    private void UpdateNewView()
    {
        if (_vm?.Library is null) return;

        var allTracks = _vm.Library.AllTracks()
            .Where(t => !string.IsNullOrEmpty(t.YouTubeId))
            .Select(t => t.ToSnapshot())
            .ToList();

        // 1. Editorial Cards (540 × 309)
        EditorialContainer.Children.Clear();
        var featured = allTracks.Take(3).ToList();
        if (featured.Count == 0 && _vm.HomeDiscovery is not null)
        {
            var ytCards = _vm.HomeDiscovery.Sections
                .SelectMany(s => s.Items)
                .OfType<Core.Discovery.DiscoveryItem.YouTube>()
                .Take(3)
                .ToList();

            foreach (var cardItem in ytCards)
            {
                var card = BuildEditorialCard("YouTube Music", cardItem.Card.Title, cardItem.Card.Uploader ?? "YouTube Music", cardItem.Card.ThumbnailUrl, () =>
                {
                    var snap = _vm.Library?.UpsertFromYouTube(
                        cardItem.Card.Id,
                        cardItem.Card.Title,
                        cardItem.Card.Uploader ?? "Unknown",
                        cardItem.Card.Duration ?? 0,
                        cardItem.Card.ThumbnailUrl);
                    if (snap is not null)
                        _vm.Playback?.PlayTrack(snap, [snap], QueueSource.Search);
                });
                EditorialContainer.Children.Add(card);
            }
        }
        else
        {
            foreach (var track in featured)
            {
                var card = BuildEditorialCard("Featured Song", track.Title, track.Artist, track.ArtworkUrl, () =>
                {
                    _vm.Playback?.PlayTrack(track, featured, QueueSource.Album);
                });
                EditorialContainer.Children.Add(card);
            }
        }
        EditorialSection.IsVisible = EditorialContainer.Children.Count > 0;

        // 2. Best New Songs Matrix (column min 280, height ~56)
        BestNewSongsContainer.Children.Clear();
        var bestSongs = allTracks.Skip(3).Take(12).ToList();
        if (bestSongs.Count == 0) bestSongs = allTracks.Take(12).ToList();

        foreach (var song in bestSongs)
        {
            var row = BuildBestSongRow(song, bestSongs);
            BestNewSongsContainer.Children.Add(row);
        }
        BestNewSongsSection.IsVisible = bestSongs.Count > 0;

        // 3. Situational Shelves
        SituationalShelvesContainer.Children.Clear();
        if (_vm.Situational is not null)
        {
            var situationalSections = _vm.Situational.Compute();
            foreach (var section in situationalSections)
            {
                if (section.Items.Count == 0) continue;
                SituationalShelvesContainer.Children.Add(BuildSituationalShelf(section));
            }
        }
    }

    private Control BuildEditorialCard(string eyebrow, string title, string artist, string? artworkUrl, Action onPlay)
    {
        var card = new EditorialCardControl
        {
            Eyebrow = eyebrow,
            CardTitle = title,
            CardSubtitle = artist,
            ArtworkUrl = artworkUrl,
            CardWidth = 540,
            ImageHeight = 309,
            Margin = new Avalonia.Thickness(0, 0, 20, 0)
        };
        card.CardSelected += (_, _) => onPlay();
        card.PlayRequested += (_, _) => onPlay();
        return card;
    }

    private Control BuildBestSongRow(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context)
    {
        var row = new CompactTrackRowControl
        {
            TrackTitle = track.Title,
            TrackArtist = track.Artist,
            ArtworkUrl = track.ArtworkUrl,
            Width = 360,
            Margin = new Avalonia.Thickness(8, 4)
        };
        row.PlayRequested += (_, _) =>
        {
            _vm?.Playback?.PlayTrack(track, context, QueueSource.Album);
        };
        return row;
    }

    private Control BuildSituationalShelf(SituationalSection section)
    {
        var panel = new StackPanel { Spacing = 13 };

        // Header
        var header = new StackPanel { Spacing = 3 };
        header.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeBrushes.TextPrimaryBrush
        });
        if (!string.IsNullOrEmpty(section.Subtitle))
        {
            header.Children.Add(new TextBlock
            {
                Text = section.Subtitle,
                FontSize = 13,
                Foreground = ThemeBrushes.TextSecondaryBrush
            });
        }
        panel.Children.Add(header);

        // Horizontal carousel of 184×184 standard cards
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var track in section.Items)
        {
            row.Children.Add(BuildSquareCard(track, section.Items));
        }
        scroll.Content = row;
        panel.Children.Add(scroll);

        return panel;
    }

    private Control BuildSquareCard(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context)
    {
        var card = new AlbumCardControl
        {
            CardTitle = track.Title,
            CardSubtitle = track.Artist,
            ArtworkUrl = track.ArtworkUrl,
            IsYouTube = !string.IsNullOrEmpty(track.YouTubeId),
            CardStyle = AlbumCardPresentationStyle.Standard,
            CardWidth = 184,
            ArtworkHeight = 184,
            Margin = new Avalonia.Thickness(0, 0, 18, 0)
        };
        card.CardSelected += (_, _) => _vm?.Playback?.PlayTrack(track, context, QueueSource.Album);
        card.PlayRequested += (_, _) => _vm?.Playback?.PlayTrack(track, context, QueueSource.Album);
        return card;
    }
}
