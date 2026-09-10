using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Discovery;
using Muses.Core.Domain;
using Muses.Core.Queue;
using Muses.Infrastructure.Discovery;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class HomeView : UserControl
{
    private ShellViewModel? _vm;

    public HomeView()
    {
        InitializeComponent();
        foreach (var mood in YouTubeHomeMood.All)
        {
            var btn = new Button
            {
                Classes = { "mood" },
                Tag = mood,
                Content = new TextBlock { Text = mood.LocalizedTitle }
            };
            Avalonia.Automation.AutomationProperties.SetName(btn, mood.LocalizedTitle);
            btn.Click += OnMoodChipClicked;
            MoodChipsContainer.Children.Add(btn);
        }
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm?.HomeDiscovery is not null)
        {
            _vm.HomeDiscovery.Changed -= OnDiscoveryChanged;
        }

        _vm = DataContext as ShellViewModel;
        if (_vm?.HomeDiscovery is not null)
        {
            _vm.HomeDiscovery.Changed += OnDiscoveryChanged;
            UpdateDiscoveryView();
        }
    }

    private void OnDiscoveryChanged()
    {
        Dispatcher.UIThread.Post(UpdateDiscoveryView);
    }

    private void UpdateDiscoveryView()
    {
        if (_vm?.HomeDiscovery is null) return;

        // 1. Resolve Top Picks
        var sections = _vm.HomeDiscovery.Sections;
        var firstSection = sections.FirstOrDefault(s => s.Items.Count > 0);
        var hero = firstSection?.Items.FirstOrDefault();
        var mixed = sections.Skip(1).Take(2).SelectMany(s => s.Items);
        var recent = sections.TakeLast(2).SelectMany(s => s.Items);

        var topPicks = TopPicksResolver.Picks(hero, mixed, recent, max: 6);
        TopPicksContainer.Children.Clear();
        foreach (var pick in topPicks)
        {
            TopPicksContainer.Children.Add(BuildPortraitCard(pick, topPicks));
        }
        TopPicksSection.IsVisible = topPicks.Count > 0;

        // 2. Build Shelves
        ShelvesContainer.Children.Clear();
        foreach (var section in sections)
        {
            if (section.Items.Count == 0 && section.Status is not SectionStatus.Loading)
                continue;

            var shelf = BuildShelf(section);
            ShelvesContainer.Children.Add(shelf);
        }
    }

    private Control BuildShelf(HomeSection section)
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

        if (section.Kind == HomeSectionKind.QuickPicks)
        {
            // 2-column grid of quick picks
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
            };

            var items = section.Items.Take(8).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var row = i % 4;
                var col = i / 4;

                var btn = BuildQuickRow(item, section.Items);
                btn.Margin = new Avalonia.Thickness(col == 0 ? 0 : 8, 4, col == 0 ? 8 : 0, 4);
                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                grid.Children.Add(btn);
            }
            panel.Children.Add(grid);
        }
        else
        {
            // Horizontal carousel
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 0, 56, 0) };
            foreach (var item in section.Items)
            {
                row.Children.Add(BuildSquareCard(item, section.Items));
            }
            scroll.Content = row;
            panel.Children.Add(WrapHorizontalFade(scroll));
        }

        return panel;
    }

    private static Control WrapHorizontalFade(Control inner)
    {
        var host = new Panel();
        host.Children.Add(inner);
        var page = ThemeBrushes.Page as ISolidColorBrush;
        var c = page?.Color ?? Color.FromRgb(0x1F, 0x1F, 0x1F);
        host.Children.Add(new Border
        {
            Width = 56,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(230, c.R, c.G, c.B), 1)
                }
            }
        });
        return host;
    }

    private Control BuildSquareCard(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings)
    {
        var (title, uploader, artworkUrl) = ExtractItemDisplay(item);
        var card = new AlbumCardControl
        {
            CardTitle = title,
            CardSubtitle = uploader,
            ArtworkUrl = artworkUrl,
            IsYouTube = true,
            CardStyle = AlbumCardPresentationStyle.Standard,
            CardWidth = 184,
            ArtworkHeight = 184,
            Margin = new Avalonia.Thickness(0, 0, 18, 0)
        };
        card.CardSelected += (_, _) => PlayDiscoveryItem(item, siblings);
        card.PlayRequested += (_, _) => PlayDiscoveryItem(item, siblings);
        return card;
    }

    private Control BuildQuickRow(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings)
    {
        var (title, uploader, artworkUrl) = ExtractItemDisplay(item);
        var row = new CompactTrackRowControl
        {
            TrackTitle = title,
            TrackArtist = uploader,
            ArtworkUrl = artworkUrl,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.PlayRequested += (_, _) => PlayDiscoveryItem(item, siblings);
        return row;
    }

    private static (string Title, string Uploader, string? ArtworkUrl) ExtractItemDisplay(DiscoveryItem item)
    {
        return item switch
        {
            DiscoveryItem.YouTube yt => (yt.Card.Title, yt.Card.Uploader ?? "YouTube Music", yt.Card.ThumbnailUrl),
            DiscoveryItem.Track tr => (tr.Snapshot.Title, tr.Snapshot.Artist, tr.Snapshot.ArtworkUrl),
            _ => ("", "", null)
        };
    }

    private void PlayDiscoveryItem(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings)
    {
        if (_vm?.Playback is null || _vm.Library is null) return;

        var contextSnapshots = new List<TrackSnapshot>();
        TrackSnapshot? selectedSnapshot = null;

        foreach (var sib in siblings)
        {
            TrackSnapshot snap;
            if (sib is DiscoveryItem.Track tr)
            {
                snap = tr.Snapshot;
            }
            else if (sib is DiscoveryItem.YouTube yt)
            {
                snap = _vm.Library.UpsertFromYouTube(
                    yt.Card.Id,
                    yt.Card.Title,
                    yt.Card.Uploader ?? "Unknown",
                    yt.Card.Duration ?? 0,
                    yt.Card.ThumbnailUrl);
            }
            else continue;

            contextSnapshots.Add(snap);
            if (sib.Id == item.Id)
            {
                selectedSnapshot = snap;
            }
        }

        if (selectedSnapshot is not null)
        {
            _vm.Playback.PlayTrack(selectedSnapshot, contextSnapshots, QueueSource.Search);
        }
    }

    private Control BuildPortraitCard(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings)
    {
        var (title, uploader, artworkUrl) = ExtractItemDisplay(item);
        var card = new AlbumCardControl
        {
            CardTitle = title,
            CardSubtitle = uploader,
            ArtworkUrl = artworkUrl,
            IsYouTube = true,
            CardStyle = AlbumCardPresentationStyle.Home,
            CardWidth = 176,
            ArtworkHeight = 176,
            Margin = new Avalonia.Thickness(0, 0, 18, 0)
        };
        card.CardSelected += (_, _) => PlayDiscoveryItem(item, siblings);
        card.PlayRequested += (_, _) => PlayDiscoveryItem(item, siblings);
        return card;
    }

    private void OnMoodChipClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: YouTubeHomeMood mood } && _vm is not null)
        {
            _vm.OpenSearchWindowWithScope(mood.SearchQuery, Core.Search.GlobalSearchScope.YouTube);
        }
    }

    private void OnRetryClicked(object? sender, RoutedEventArgs e)
    {
        _vm?.HomeDiscovery?.Reload();
    }
}
