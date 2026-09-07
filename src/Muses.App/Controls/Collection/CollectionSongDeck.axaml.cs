using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Muses.App.Services;
using Muses.Core.Chrome;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;
using Muses.App.Theme;

namespace Muses.App.Controls.Collection;

public partial class CollectionSongDeck : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<CollectionTrackRow>?> RowsProperty =
        AvaloniaProperty.Register<CollectionSongDeck, IReadOnlyList<CollectionTrackRow>?>(nameof(Rows));

    public static readonly StyledProperty<TrackSnapshot?> CurrentTrackProperty =
        AvaloniaProperty.Register<CollectionSongDeck, TrackSnapshot?>(nameof(CurrentTrack));

    public static readonly StyledProperty<IReadOnlyList<PlaylistEntity>?> PlaylistsProperty =
        AvaloniaProperty.Register<CollectionSongDeck, IReadOnlyList<PlaylistEntity>?>(nameof(Playlists));

    public static readonly StyledProperty<bool> IsInteractionEnabledProperty =
        AvaloniaProperty.Register<CollectionSongDeck, bool>(nameof(IsInteractionEnabled), true);

    public IReadOnlyList<CollectionTrackRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
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

    public bool IsInteractionEnabled
    {
        get => GetValue(IsInteractionEnabledProperty);
        set => SetValue(IsInteractionEnabledProperty, value);
    }

    public event Action<CollectionTrackRow>? TrackActivated;
    public event Action? ExpandRequested;
    public event Action<CollectionTrackRow>? RemoveRequested;

    private double _position = 0;
    private int? _hoveredIndex;
    private bool _fanHovered;
    private bool _isDeckDragging;
    private Point _deckDragStartPoint;
    private double _deckDragStartPosition;

    private bool _isScrubberDragging;
    private bool _isScrubberHovering;

    private CollectionDeckGeometry _geometry = CollectionDeckGeometry.Resolve(1000, 800);

    public int FocusedIndex
    {
        get
        {
            var count = Rows?.Count ?? 0;
            if (count <= 0) return 0;
            return Math.Min(count - 1, Math.Max(0, (int)Math.Round(_position)));
        }
    }

    public CollectionSongDeck()
    {
        InitializeComponent();

        PrevButton.Click += (_, _) => MoveFocusBy(-1);
        NextButton.Click += (_, _) => MoveFocusBy(1);
        ExpandButton.Click += (_, _) => ExpandRequested?.Invoke();

        StagePanel.PointerEntered += (_, _) => { _fanHovered = true; RenderDeck(); };
        StagePanel.PointerExited += (_, _) => { _fanHovered = false; _hoveredIndex = null; RenderDeck(); };
        StagePanel.PointerWheelChanged += OnStageWheelChanged;
        StagePanel.PointerPressed += OnStagePointerPressed;
        StagePanel.PointerMoved += OnStagePointerMoved;
        StagePanel.PointerReleased += OnStagePointerReleased;

        ScrubberCanvas.PointerEntered += (_, _) => { _isScrubberHovering = true; UpdateScrubberValueText(); };
        ScrubberCanvas.PointerExited += (_, _) => { _isScrubberHovering = false; UpdateScrubberValueText(); };
        ScrubberCanvas.PointerPressed += OnScrubberPointerPressed;
        ScrubberCanvas.PointerMoved += OnScrubberPointerMoved;
        ScrubberCanvas.PointerReleased += OnScrubberPointerReleased;

        SizeChanged += (_, _) => RecomputeAndRender();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowsProperty)
        {
            _position = 0;
            _hoveredIndex = null;
            RecomputeAndRender();
        }
        else if (change.Property == CurrentTrackProperty)
        {
            RenderDeck();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInteractionEnabled) return;

        switch (e.Key)
        {
            case Key.Left:
                MoveFocusBy(-1);
                e.Handled = true;
                break;
            case Key.Right:
                MoveFocusBy(1);
                e.Handled = true;
                break;
            case Key.Home:
                MoveFocusTo(0);
                e.Handled = true;
                break;
            case Key.End:
                var count = Rows?.Count ?? 0;
                if (count > 0) MoveFocusTo(count - 1);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                ActivateFocused();
                e.Handled = true;
                break;
        }
    }

    private void MoveFocusBy(int delta)
    {
        MoveFocusTo(FocusedIndex + delta);
    }

    public void MoveFocusTo(int targetIndex)
    {
        var count = Rows?.Count ?? 0;
        if (count <= 0) return;
        _position = Math.Min(count - 1, Math.Max(0, targetIndex));
        RenderDeck();
        RenderScrubber();
    }

    private void ActivateFocused()
    {
        var rows = Rows;
        if (rows is null || rows.Count == 0) return;
        var index = FocusedIndex;
        if (index >= 0 && index < rows.Count)
        {
            TrackActivated?.Invoke(rows[index]);
        }
    }

    private void RecomputeAndRender()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0) width = 1000;
        if (height <= 0) height = 800;

        _geometry = CollectionDeckGeometry.Resolve(width, height);
        StagePanel.Height = _geometry.ViewportHeight;

        RenderDeck();
        RenderScrubber();
    }

    private void RenderDeck()
    {
        DeckCanvas.Children.Clear();
        var rows = Rows;
        var count = rows?.Count ?? 0;
        if (count == 0 || rows is null)
        {
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            return;
        }

        var focused = FocusedIndex;
        PrevButton.IsEnabled = focused > 0 && IsInteractionEnabled;
        NextButton.IsEnabled = focused < count - 1 && IsInteractionEnabled;

        var availableWidth = Bounds.Width > 0 ? Bounds.Width : 1000;
        DeckCanvas.Width = availableWidth;
        DeckCanvas.Height = _geometry.ViewportHeight;

        var visibleIndices = CollectionDeckProjection.VisibleIndices(count, _position, _geometry.Radius);
        var centerX = availableWidth / 2.0;

        foreach (var index in visibleIndices)
        {
            var row = rows[index];
            var relative = index - _position;
            var distance = Math.Abs(relative);
            var hovered = _hoveredIndex == index;
            var isPlaying = row.Matches(CurrentTrack);

            var fanScale = _fanHovered ? 1.06 : 1.0;
            var spread = _geometry.Spread * fanScale;
            var x = relative * spread;
            var y = 1.7 * relative * relative + 5.5 * distance;
            var scale = 1.0 - Math.Min(distance * 0.018, 0.08);

            if (hovered)
            {
                x += relative == 0 ? 3 : (relative < 0 ? -3 : 3);
                y -= AppleMusicTokens.CollectionDeckHoverLift;
                scale *= 1.035;
            }

            var playingLift = isPlaying && !MotionChrome.PreferReducedMotion ? 14.0 : 0.0;
            var playingScale = isPlaying && !MotionChrome.PreferReducedMotion ? 1.05 : 1.0;
            var rotation = relative * 3.15;
            var zIndex = isPlaying ? 600 : (hovered ? 500 : 200 - (int)(distance * 10));

            var card = CreateCard(row, index, isPlaying, hovered, index == focused);
            card.Width = _geometry.CardWidth;
            card.Height = _geometry.CardHeight;

            var transform = new TransformGroup();
            transform.Children.Add(new RotateTransform(rotation));
            transform.Children.Add(new ScaleTransform(scale * playingScale, scale * playingScale));
            card.RenderTransform = transform;
            card.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

            Canvas.SetLeft(card, centerX - _geometry.CardWidth / 2.0 + x);
            Canvas.SetTop(card, 18.0 + y - playingLift);
            card.ZIndex = zIndex;

            DeckCanvas.Children.Add(card);
        }
    }

    private Control CreateCard(CollectionTrackRow row, int index, bool isPlaying, bool isHovered, bool isFocused)
    {
        var totalHeight = _geometry.CardHeight;
        var cardWidth = _geometry.CardWidth;

        var root = new Border
        {
            Width = cardWidth,
            Height = totalHeight,
            CornerRadius = new CornerRadius(22),
            ClipToBounds = true,
            Background = ThemeBrushes.SurfaceBrush,
            Cursor = Cursor.Parse("Hand")
        };

        // Border stroke
        if (isPlaying)
        {
            root.BorderBrush = ThemeBrushes.AccentOverlayD9;
            root.BorderThickness = new Thickness(1.5);
            root.BoxShadow = BoxShadows.Parse($"0 0 22 0 #85FA586A, 0 8 16 0 #59000000");
        }
        else if (isFocused)
        {
            root.BorderBrush = ThemeBrushes.WhiteOverlay61;
            root.BorderThickness = new Thickness(1.5);
            root.BoxShadow = BoxShadows.Parse("0 6 14 0 #40000000");
        }
        else if (isHovered)
        {
            root.BorderBrush = ThemeBrushes.WhiteOverlay47;
            root.BorderThickness = new Thickness(1.0);
            root.BoxShadow = BoxShadows.Parse("0 10 18 0 #73000000");
        }
        else
        {
            root.BorderBrush = ThemeBrushes.HairlineBrush;
            root.BorderThickness = new Thickness(1.0);
            root.BoxShadow = BoxShadows.Parse("0 4 8 0 #33000000");
        }

        var panel = new Panel();

        // 1. Full-bleed Artwork
        var art = new ArtworkControl
        {
            SourceUrl = row.Snapshot.ArtworkUrl,
            CornerRadius = new CornerRadius(22),
            GlyphSize = Math.Max(32, cardWidth * 0.22),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        panel.Children.Add(art);

        // 2. Top-Right Badges: YouTube mark + ellipsis
        var topBadges = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0)
        };

        if (!string.IsNullOrEmpty(row.Snapshot.YouTubeId))
        {
            var ytBadge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = ThemeBrushes.BlackOverlay99,
                BorderBrush = ThemeBrushes.WhiteOverlay40,
                BorderThickness = new Thickness(0.75),
                Child = new YouTubeMark { MarkSize = 12 }
            };
            topBadges.Children.Add(ytBadge);
        }

        var menuBadge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = ThemeBrushes.BlackOverlay99,
            BorderBrush = ThemeBrushes.WhiteOverlay40,
            BorderThickness = new Thickness(0.75),
            Child = new TextBlock
            {
                Text = "•••",
                FontSize = 8,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        menuBadge.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            ShowContextMenu(row, menuBadge);
        };
        topBadges.Children.Add(menuBadge);
        panel.Children.Add(topBadges);

        // 3. Bottom Gradient Scrim
        var scrim = new Border
        {
            Height = totalHeight * 0.58,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
                    new GradientStop(Color.FromArgb(115, 0, 0, 0), 0.40),
                    new GradientStop(Color.FromArgb(217, 0, 0, 0), 0.75),
                    new GradientStop(Color.FromArgb(245, 0, 0, 0), 1.0)
                }
            }
        };
        panel.Children.Add(scrim);

        // 4. Bottom Content: Title, Artist, Action Row
        var content = new StackPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Margin = new Thickness(12, 0, 12, 10),
            Spacing = 4
        };

        var titleBlock = new TextBlock
        {
            Text = row.Title,
            FontSize = _geometry.FooterHeight <= 52 ? 12.5 : 14.5,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        content.Children.Add(titleBlock);

        var artistBlock = new TextBlock
        {
            Text = row.Artist,
            FontSize = _geometry.FooterHeight <= 52 ? 10.5 : 12.0,
            FontWeight = FontWeight.Medium,
            Foreground = ThemeBrushes.WhiteOverlayD1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        content.Children.Add(artistBlock);

        // Action Row: Song Tag on Left, Play/Pause Pill on Right
        var actionGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var tagPill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = ThemeBrushes.WhiteOverlay24,
            Padding = new Thickness(6, 3),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 3,
                Children =
                {
                    new PathIcon
                    {
                        Data = Geometry.Parse("M12,3V13.55C11.41,13.21 10.73,13 10,13C7.79,13 6,14.79 6,17C6,19.21 7.79,21 10,21C12.21,21 14,19.21 14,17V7H18V3H12Z"),
                        Width = 9,
                        Height = 9,
                        Foreground = isPlaying ? ThemeBrushes.Accent : ThemeBrushes.WhiteOverlayCC
                    },
                    new TextBlock
                    {
                        Text = L10n.Tr("SONG", "歌曲"),
                        FontSize = 9,
                        FontWeight = FontWeight.Bold,
                        Foreground = ThemeBrushes.WhiteOverlayE6
                    }
                }
            }
        };
        actionGrid.Children.Add(tagPill);

        var playPill = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = (isPlaying || isHovered) ? ThemeBrushes.Accent : ThemeBrushes.WhiteOverlay38,
            BorderBrush = ThemeBrushes.WhiteOverlay59,
            BorderThickness = new Thickness(0.75),
            Padding = new Thickness(8, 3.5),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new PathIcon
                    {
                        Data = Geometry.Parse(isPlaying ? "M14,19H18V5H14M6,19H10V5H6V19Z" : "M8,5.14V19.14L19,12.14L8,5.14Z"),
                        Width = 9,
                        Height = 9,
                        Foreground = Brushes.White
                    },
                    new TextBlock
                    {
                        Text = isPlaying ? L10n.Tr("Pause", "暂停") : L10n.Tr("Play", "播放"),
                        FontSize = 9.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    }
                }
            }
        };
        Grid.SetColumn(playPill, 2);
        actionGrid.Children.Add(playPill);

        content.Children.Add(actionGrid);
        panel.Children.Add(content);
        root.Child = panel;

        // Pointer interactions on card
        root.PointerEntered += (_, _) =>
        {
            _hoveredIndex = index;
            RenderDeck();
        };

        root.PointerExited += (_, _) =>
        {
            if (_hoveredIndex == index)
            {
                _hoveredIndex = null;
                RenderDeck();
            }
        };

        root.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(root).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                ShowContextMenu(row, root);
                return;
            }

            if (e.GetCurrentPoint(root).Properties.IsLeftButtonPressed)
            {
                if (index != FocusedIndex)
                {
                    MoveFocusTo(index);
                }
                else
                {
                    TrackActivated?.Invoke(row);
                }
                e.Handled = true;
            }
        };

        return root;
    }

    private void ShowContextMenu(CollectionTrackRow row, Control target)
    {
        // Construct and open TrackContextMenu
        var menu = TrackContextMenu.Create(
            row.Snapshot,
            null, // Handled via callback or shell
            null,
            null,
            null,
            onPlay: () => TrackActivated?.Invoke(row),
            onRemoveFromContainer: RemoveRequested != null ? () => RemoveRequested(row) : null);

        menu.Open(target);
    }

    private void RenderScrubber()
    {
        ScrubberCanvas.Children.Clear();
        var rows = Rows;
        var count = rows?.Count ?? 0;
        if (count == 0 || rows is null)
        {
            ScrubberContainer.IsVisible = false;
            return;
        }

        ScrubberContainer.IsVisible = true;
        var availableWidth = Bounds.Width > 0 ? Bounds.Width : 1000;
        var scrubberWidth = CollectionDeckScrubberMetrics.Width(availableWidth);
        ScrubberCanvas.Width = scrubberWidth;

        // Background track
        var track = new Border
        {
            Width = scrubberWidth,
            Height = CollectionDeckScrubberMetrics.TrackHeight,
            CornerRadius = new CornerRadius(2),
            Background = ThemeBrushes.WhiteOverlay38,
            BorderBrush = ThemeBrushes.WhiteOverlay14,
            BorderThickness = new Thickness(0.5),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Canvas.SetTop(track, (CollectionDeckScrubberMetrics.ControlHeight - CollectionDeckScrubberMetrics.TrackHeight) / 2.0);
        ScrubberCanvas.Children.Add(track);

        // Thumb
        var thumbX = CollectionDeckScrubberMetrics.ThumbCenterX(_position, count, scrubberWidth);
        var thumb = new Border
        {
            Width = CollectionDeckScrubberMetrics.ThumbWidth,
            Height = CollectionDeckScrubberMetrics.ThumbHeight,
            CornerRadius = new CornerRadius(CollectionDeckScrubberMetrics.ThumbHeight / 2.0),
            Background = ThemeBrushes.SurfaceChrome,
            BorderBrush = ThemeBrushes.WhiteOverlayB3,
            BorderThickness = new Thickness(1.0),
            BoxShadow = BoxShadows.Parse("0 2 5 0 #33000000"),
            Cursor = Cursor.Parse("Hand")
        };
        Canvas.SetLeft(thumb, thumbX - CollectionDeckScrubberMetrics.ThumbWidth / 2.0);
        Canvas.SetTop(thumb, (CollectionDeckScrubberMetrics.ControlHeight - CollectionDeckScrubberMetrics.ThumbHeight) / 2.0);
        ScrubberCanvas.Children.Add(thumb);

        UpdateScrubberValueText();
    }

    private void UpdateScrubberValueText()
    {
        var rows = Rows;
        var count = rows?.Count ?? 0;
        var shows = _isScrubberDragging || _isScrubberHovering;
        ScrubberValueText.Opacity = shows ? 1.0 : 0.0;

        if (count > 0 && rows is not null)
        {
            var idx = FocusedIndex;
            if (idx >= 0 && idx < rows.Count)
            {
                ScrubberValueText.Text = $"{idx + 1} / {count} · {rows[idx].Title}";
            }
        }
    }

    private void OnStageWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsInteractionEnabled) return;
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (delta > 0.1) MoveFocusBy(-1);
        else if (delta < -0.1) MoveFocusBy(1);
        e.Handled = true;
    }

    private void OnStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInteractionEnabled) return;
        if (e.GetCurrentPoint(StagePanel).Properties.IsLeftButtonPressed)
        {
            _isDeckDragging = true;
            _deckDragStartPoint = e.GetPosition(StagePanel);
            _deckDragStartPosition = _position;
            e.Pointer.Capture(StagePanel);
        }
    }

    private void OnStagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDeckDragging) return;
        var current = e.GetPosition(StagePanel);
        var deltaX = current.X - _deckDragStartPoint.X;
        var count = Rows?.Count ?? 0;
        if (count <= 0 || _geometry.Spread <= 0) return;

        var target = _deckDragStartPosition - deltaX / _geometry.Spread;
        _position = Math.Min(count - 1, Math.Max(0, target));
        RenderDeck();
        RenderScrubber();
    }

    private void OnStagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDeckDragging)
        {
            _isDeckDragging = false;
            e.Pointer.Capture(null);
            var count = Rows?.Count ?? 0;
            if (count > 0)
            {
                _position = Math.Round(_position);
                RenderDeck();
                RenderScrubber();
            }
        }
    }

    private void OnScrubberPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInteractionEnabled) return;
        var count = Rows?.Count ?? 0;
        if (count <= 0) return;

        _isScrubberDragging = true;
        e.Pointer.Capture(ScrubberCanvas);

        var loc = e.GetPosition(ScrubberCanvas);
        var scrubberWidth = ScrubberCanvas.Width;
        var pos = CollectionDeckScrubberMetrics.Position(loc.X, count, scrubberWidth);
        _position = pos;
        RenderDeck();
        RenderScrubber();
    }

    private void OnScrubberPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isScrubberDragging) return;
        var count = Rows?.Count ?? 0;
        if (count <= 0) return;

        var loc = e.GetPosition(ScrubberCanvas);
        var scrubberWidth = ScrubberCanvas.Width;
        var pos = CollectionDeckScrubberMetrics.Position(loc.X, count, scrubberWidth);
        _position = pos;
        RenderDeck();
        RenderScrubber();
    }

    private void OnScrubberPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isScrubberDragging)
        {
            _isScrubberDragging = false;
            e.Pointer.Capture(null);
            var count = Rows?.Count ?? 0;
            if (count > 0)
            {
                _position = Math.Round(_position);
                RenderDeck();
                RenderScrubber();
            }
        }
    }
}
