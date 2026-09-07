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
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class CatalogArtistDetailView : UserControl
{
    private ShellViewModel? _vm;
    private CatalogArtistProjection? _currentArtist;

    public CatalogArtistDetailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                Refresh();
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm = DataContext as ShellViewModel;
        Refresh();
    }

    public void Refresh()
    {
        if (_vm?.SelectedArtist is null) return;
        var artist = _vm.SelectedArtist;
        _currentArtist = artist;

        ArtistNameText.Text = artist.Name;
        ArtistStatsText.Text = $"{artist.Releases.Count} releases • {artist.Tracks.Count} library tracks";
        ArtistAvatar.SourceUrl = artist.ArtworkUrl ?? artist.Tracks.FirstOrDefault()?.ArtworkUrl;

        // 1. Top Songs
        TopSongsContainer.Children.Clear();
        foreach (var track in artist.Tracks.Take(8))
        {
            TopSongsContainer.Children.Add(BuildTrackRow(track, artist.Tracks));
        }
        TopSongsSection.IsVisible = artist.Tracks.Count > 0;

        // 2. Albums & Singles
        AlbumsContainer.Children.Clear();
        SinglesContainer.Children.Clear();

        var albums = artist.Releases.Where(r => r.Kind == CatalogReleaseKind.Album).ToList();
        var singles = artist.Releases.Where(r => r.Kind == CatalogReleaseKind.Single || r.Kind == CatalogReleaseKind.Ep).ToList();

        foreach (var rel in albums) AlbumsContainer.Children.Add(BuildReleaseCard(rel));
        foreach (var rel in singles) SinglesContainer.Children.Add(BuildReleaseCard(rel));

        AlbumsSection.IsVisible = albums.Count > 0;
        SinglesSection.IsVisible = singles.Count > 0;

        // 3. Best-effort online discography fetch
        if (_vm.Catalog is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var online = await _vm.Catalog.FetchArtistOnlineDiscographyAsync(artist).ConfigureAwait(false);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_currentArtist?.StableId != artist.StableId) return;

                        if (online.Albums.Count > 0 && albums.Count == 0)
                        {
                            AlbumsContainer.Children.Clear();
                            foreach (var item in online.Albums)
                            {
                                AlbumsContainer.Children.Add(BuildOnlineReleaseCard(item, artist.Name));
                            }
                            AlbumsSection.IsVisible = true;
                        }

                        if (online.SinglesAndEPs.Count > 0 && singles.Count == 0)
                        {
                            SinglesContainer.Children.Clear();
                            foreach (var item in online.SinglesAndEPs)
                            {
                                SinglesContainer.Children.Add(BuildOnlineReleaseCard(item, artist.Name));
                            }
                            SinglesSection.IsVisible = true;
                        }
                    });
                }
                catch
                {
                    // Online discography is best-effort
                }
            });
        }
    }

    private Button BuildTrackRow(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context)
    {
        var btn = new Button
        {
            Classes = { "discRow" },
            Tag = track
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var art = new ArtworkControl
        {
            Width = 44,
            Height = 44,
            CornerRadius = new Avalonia.CornerRadius(6),
            GlyphSize = 16,
            SourceUrl = track.ArtworkUrl
        };
        Grid.SetColumn(art, 0);
        grid.Children.Add(art);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Margin = new Avalonia.Thickness(12, 0, 8, 0)
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = track.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = track.AlbumTitle ?? track.Artist,
            FontSize = 11,
            Foreground = ThemeBrushes.TextSecondaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var playIcon = new PathIcon
        {
            Data = StreamGeometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z"),
            Width = 12,
            Height = 12,
            Foreground = ThemeBrushes.TextSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(playIcon, 2);
        grid.Children.Add(playIcon);

        btn.Content = grid;
        btn.Click += (_, _) => _vm?.Playback?.PlayTrack(track, context, QueueSource.Artist);
        return btn;
    }

    private Button BuildReleaseCard(CatalogReleaseProjection release)
    {
        var btn = new Button
        {
            Classes = { "chromeBtn" },
            Width = 160,
            Height = 210,
            Tag = release
        };

        var stack = new StackPanel { Spacing = 6 };
        var art = new ArtworkControl
        {
            Width = 160,
            Height = 160,
            CornerRadius = new Avalonia.CornerRadius(8),
            GlyphSize = 32,
            SourceUrl = release.ArtworkUrl
        };
        stack.Children.Add(art);

        stack.Children.Add(new TextBlock
        {
            Text = release.Title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        stack.Children.Add(new TextBlock
        {
            Text = release.Year?.ToString() ?? "",
            FontSize = 11,
            Foreground = ThemeBrushes.TextSecondaryBrush
        });

        btn.Content = stack;
        btn.Click += (_, _) => _vm?.SelectRelease(release);
        return btn;
    }

    private Button BuildOnlineReleaseCard(OnlineReleaseItem item, string artistName)
    {
        var btn = new Button
        {
            Classes = { "chromeBtn" },
            Width = 160,
            Height = 210
        };

        var stack = new StackPanel { Spacing = 6 };
        var art = new ArtworkControl
        {
            Width = 160,
            Height = 160,
            CornerRadius = new Avalonia.CornerRadius(8),
            GlyphSize = 32,
            SourceUrl = item.ArtworkUrl
        };
        stack.Children.Add(art);

        stack.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        stack.Children.Add(new TextBlock
        {
            Text = item.Year?.ToString() ?? "",
            FontSize = 11,
            Foreground = ThemeBrushes.TextSecondaryBrush
        });

        btn.Content = stack;
        btn.Click += (_, _) =>
        {
            if (_vm?.Catalog is not null)
            {
                var rel = _vm.Catalog.Release(item.StableId);
                if (rel is not null) _vm.SelectRelease(rel);
            }
        };
        return btn;
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        _vm?.CloseArtistDetail();
    }

    private void OnPlayAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Playback is null || _currentArtist is null || _currentArtist.Tracks.Count == 0) return;
        _vm.Playback.PlayTrack(_currentArtist.Tracks[0], _currentArtist.Tracks, QueueSource.Artist);
    }

    private void OnShuffleClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Playback is null || _currentArtist is null || _currentArtist.Tracks.Count == 0) return;
        var list = _currentArtist.Tracks.ToList();
        var rng = new Random();
        var n = list.Count;
        while (n > 1)
        {
            n--;
            var k = rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
        _vm.Playback.PlayTrack(list[0], list, QueueSource.Artist);
    }
}
