using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;
using Muses.Core.Playback;
using Muses.App.Theme;

namespace Muses.App.Controls.Collection;

public partial class CollectionTrackTable : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<CollectionTrackRow>?> RowsProperty =
        AvaloniaProperty.Register<CollectionTrackTable, IReadOnlyList<CollectionTrackRow>?>(nameof(Rows));

    public static readonly StyledProperty<CollectionTableDefaultSort> DefaultSortProperty =
        AvaloniaProperty.Register<CollectionTrackTable, CollectionTableDefaultSort>(nameof(DefaultSort), CollectionTableDefaultSort.TitleAZ);

    public static readonly StyledProperty<TrackSnapshot?> CurrentTrackProperty =
        AvaloniaProperty.Register<CollectionTrackTable, TrackSnapshot?>(nameof(CurrentTrack));

    public static readonly StyledProperty<IReadOnlyList<PlaylistEntity>?> PlaylistsProperty =
        AvaloniaProperty.Register<CollectionTrackTable, IReadOnlyList<PlaylistEntity>?>(nameof(Playlists));

    public IReadOnlyList<CollectionTrackRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public CollectionTableDefaultSort DefaultSort
    {
        get => GetValue(DefaultSortProperty);
        set => SetValue(DefaultSortProperty, value);
    }

    public TrackSnapshot? CurrentTrack
    {
        get => GetValue(CurrentTrackProperty);
        set => SetValue(CurrentTrackProperty, value);
    }

    public IReadOnlyList<PlaylistEntity>? Playlists
    {
        get => GetValue(PlaylistsProperty);
        set => SetValue(PlaylistsProperty, value);
    }

    public LibraryService? Library { get; set; }
    public PlaybackService? Playback { get; set; }
    public PlaylistService? PlaylistService { get; set; }

    public event Action<CollectionTrackRow>? TrackActivated;
    public event Action? CollapseRequested;
    public event Action<CollectionTrackRow>? RemoveRequested;

    private CollectionColumn? _sortColumn;
    private bool _sortAscending = true;

    public CollectionTrackTable()
    {
        InitializeComponent();

        CollapseButton.Click += (_, _) => CollapseRequested?.Invoke();

        HeaderOrder.Click += (_, _) => ToggleSort(CollectionColumn.Order);
        HeaderTitle.Click += (_, _) => ToggleSort(CollectionColumn.Title);
        HeaderArtist.Click += (_, _) => ToggleSort(CollectionColumn.Artist);
        HeaderAlbum.Click += (_, _) => ToggleSort(CollectionColumn.Album);
        HeaderYear.Click += (_, _) => ToggleSort(CollectionColumn.Year);
        HeaderGenre.Click += (_, _) => ToggleSort(CollectionColumn.Genre);
        HeaderTime.Click += (_, _) => ToggleSort(CollectionColumn.Duration);
        HeaderAdded.Click += (_, _) => ToggleSort(CollectionColumn.AddedAt);
        HeaderPlays.Click += (_, _) => ToggleSort(CollectionColumn.Plays);

        UpdateHeaderLabels();
    }

    private void UpdateHeaderLabels()
    {
        HeaderOrder.Content = "#";
        HeaderTitle.Content = L10n.Tr("Title", "标题");
        HeaderArtist.Content = L10n.Tr("Artist", "艺术家");
        HeaderAlbum.Content = L10n.Tr("Album", "专辑");
        HeaderYear.Content = L10n.Tr("Year", "年份");
        HeaderGenre.Content = L10n.Tr("Genre", "类型");
        HeaderTime.Content = L10n.Tr("Time", "时长");
        HeaderAdded.Content = L10n.Tr("Date Added", "添加日期");
        HeaderPlays.Content = L10n.Tr("Plays", "播放次数");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowsProperty || change.Property == DefaultSortProperty || change.Property == CurrentTrackProperty)
        {
            RenderRows();
        }
    }

    private void ToggleSort(CollectionColumn column)
    {
        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
        RenderRows();
    }

    private void RenderRows()
    {
        var rows = Rows;
        if (rows is null || rows.Count == 0)
        {
            RowItemsControl.ItemsSource = null;
            return;
        }

        IReadOnlyList<CollectionTrackRow> displayedRows;
        if (_sortColumn.HasValue)
        {
            displayedRows = CollectionTrackSort.Rows(rows, _sortColumn.Value, _sortAscending);
        }
        else
        {
            displayedRows = CollectionTrackSort.Rows(rows, DefaultSort);
        }

        var controls = new List<Control>();
        foreach (var row in displayedRows)
        {
            controls.Add(CreateRowControl(row));
        }
        RowItemsControl.ItemsSource = controls;
    }

    private Control CreateRowControl(CollectionTrackRow row)
    {
        var isPlaying = row.Matches(CurrentTrack);

        var border = new Border
        {
            Height = 44,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursor.Parse("Hand"),
            Margin = new Thickness(0, 1)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("48,*,160,170,64,100,70,110,64")
        };

        // 1. Order (#)
        var orderText = new TextBlock
        {
            Text = $"{row.CanonicalIndex + 1}",
            Foreground = ThemeBrushes.WhiteOverlay80,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(orderText, 0);
        grid.Children.Add(orderText);

        // 2. Title (Artwork + Play Icon + Title + Heart)
        var titlePanel = new DockPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };

        // Artwork container with hover play button
        var artPanel = new Panel
        {
            Width = 36,
            Height = 36,
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(artPanel, Dock.Left);

        var art = new ArtworkControl
        {
            SourceUrl = row.Snapshot.ArtworkUrl,
            CornerRadius = new CornerRadius(4),
            GlyphSize = 14,
            Width = 36,
            Height = 36
        };
        artPanel.Children.Add(art);

        var playOverlay = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(4),
            Background = ThemeBrushes.BlackOverlay80,
            IsVisible = isPlaying,
            Child = new PathIcon
            {
                Data = Geometry.Parse(isPlaying ? "M14,19H18V5H14M6,19H10V5H6V19Z" : "M8,5.14V19.14L19,12.14L8,5.14Z"),
                Width = 12,
                Height = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        artPanel.Children.Add(playOverlay);
        titlePanel.Children.Add(artPanel);

        // Like Heart Button
        var isLiked = row.Snapshot.Liked;
        var heartBtn = new Button
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = Cursor.Parse("Hand"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Content = new PathIcon
            {
                Data = Geometry.Parse(isLiked
                    ? "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z"
                    : "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z"),
                Width = 13,
                Height = 13,
                Foreground = isLiked ? ThemeBrushes.Accent : ThemeBrushes.TextSecondaryMuted
            }
        };
        heartBtn.Click += (s, e) =>
        {
            e.Handled = true;
            Library?.ToggleLike(row.Id);
            // Toggle local visual state
            isLiked = !isLiked;
            if (heartBtn.Content is PathIcon p)
            {
                p.Foreground = isLiked ? ThemeBrushes.Accent : ThemeBrushes.TextSecondaryMuted;
            }
        };
        DockPanel.SetDock(heartBtn, Dock.Right);
        titlePanel.Children.Add(heartBtn);

        var titleText = new TextBlock
        {
            Text = row.Title,
            FontSize = 13,
            FontWeight = isPlaying ? FontWeight.SemiBold : FontWeight.Regular,
            Foreground = isPlaying ? ThemeBrushes.Accent : ThemeBrushes.TextPrimaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        titlePanel.Children.Add(titleText);

        Grid.SetColumn(titlePanel, 1);
        grid.Children.Add(titlePanel);

        // 3. Artist
        var artistText = CreateSecondaryText(row.Artist);
        Grid.SetColumn(artistText, 2);
        grid.Children.Add(artistText);

        // 4. Album
        var albumText = CreateSecondaryText(row.Album);
        Grid.SetColumn(albumText, 3);
        grid.Children.Add(albumText);

        // 5. Year
        var yearText = CreateSecondaryText(row.Year?.ToString() ?? "—");
        Grid.SetColumn(yearText, 4);
        grid.Children.Add(yearText);

        // 6. Genre
        var genreText = CreateSecondaryText(row.Genre ?? "—");
        Grid.SetColumn(genreText, 5);
        grid.Children.Add(genreText);

        // 7. Time (Duration)
        var durationText = CreateSecondaryText(FormatDuration(row.Duration));
        Grid.SetColumn(durationText, 6);
        grid.Children.Add(durationText);

        // 8. Date Added
        var addedText = CreateSecondaryText(row.AddedAt?.ToString("MMM d, yyyy") ?? "—");
        Grid.SetColumn(addedText, 7);
        grid.Children.Add(addedText);

        // 9. Plays
        var playsText = CreateSecondaryText(row.PlayCount.ToString());
        Grid.SetColumn(playsText, 8);
        grid.Children.Add(playsText);

        border.Child = grid;

        // Hover effect
        border.PointerEntered += (_, _) =>
        {
            border.Background = ThemeBrushes.WhiteOverlay14;
            if (!isPlaying) playOverlay.IsVisible = true;
        };

        border.PointerExited += (_, _) =>
        {
            border.Background = Brushes.Transparent;
            if (!isPlaying) playOverlay.IsVisible = false;
        };

        // Pointer click / double click / right click
        border.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                var menu = TrackContextMenu.Create(
                    row.Snapshot,
                    Playback,
                    Library,
                    PlaylistService,
                    null,
                    onPlay: () => TrackActivated?.Invoke(row),
                    onRemoveFromContainer: RemoveRequested != null ? () => RemoveRequested(row) : null);
                menu.Open(border);
                return;
            }

            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    TrackActivated?.Invoke(row);
                    e.Handled = true;
                }
            }
        };

        artPanel.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(artPanel).Properties.IsLeftButtonPressed)
            {
                TrackActivated?.Invoke(row);
                e.Handled = true;
            }
        };

        return border;
    }

    private static TextBlock CreateSecondaryText(string text) => new()
    {
        Text = string.IsNullOrEmpty(text) ? "—" : text,
        Foreground = ThemeBrushes.TextSecondaryBrush,
        FontSize = 12.5,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1
    };

    private static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return "—";
        var total = (int)Math.Round(seconds);
        if (total >= 3600)
            return $"{total / 3600}:{(total / 60) % 60:D2}:{total % 60:D2}";
        return $"{total / 60}:{total % 60:D2}";
    }
}
