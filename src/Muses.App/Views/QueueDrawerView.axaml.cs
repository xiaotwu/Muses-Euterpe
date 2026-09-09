using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.Theme;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Playback;
using Muses.Core.Queue;

namespace Muses.App.Views;

public partial class QueueDrawerView : UserControl
{
    private ShellViewModel? _vm;
    private Action<PlaybackEvent>? _eventHandler;
    private Point? _dragStart;
    private int _dragFromUpNext = -1;

    public QueueDrawerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        _vm = DataContext as ShellViewModel;
        ApplyLocalizedChrome();
        Subscribe();
        UpdateQueue();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        UpdateQueue();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            UpdateQueue();
    }

    private void ApplyLocalizedChrome()
    {
        QueueTitle.Text = L10n.Tr("Queue", "队列");
        NowPlayingHeader.Text = L10n.Tr("Now Playing", "正在播放");
        HistoryHeader.Text = L10n.Tr("History", "历史记录");
        ClearLabel.Text = L10n.Tr("Clear", "清空");
    }

    private void Subscribe()
    {
        if (_vm?.Playback is null || _eventHandler is not null) return;
        _eventHandler = _ => Dispatcher.UIThread.Post(UpdateQueue);
        _vm.Playback.EventBus.EventPosted += _eventHandler;
    }

    private void Unsubscribe()
    {
        if (_vm?.Playback is null || _eventHandler is null) return;
        _vm.Playback.EventBus.EventPosted -= _eventHandler;
        _eventHandler = null;
    }

    public void UpdateQueue()
    {
        if (_vm?.Playback is null) return;
        var queue = _vm.Playback.Queue;

        UpNextContainer.Children.Clear();
        HistoryContainer.Children.Clear();

        var upcoming = QueuePresentation.BuildUpcoming(queue);
        UpNextHeader.Text = $"{L10n.Tr("Up Next", "下一首")} ({upcoming.Count})";

        foreach (var row in upcoming)
        {
            var captured = row;
            var btn = BuildQueueRow(
                captured.Item.Track,
                onPlay: () => PlayKeepingCollectionContext(captured),
                onRemove: () =>
                {
                    _vm.RemoveUpcomingRow(captured);
                    Dispatcher.UIThread.Post(UpdateQueue);
                },
                upNextIndex: captured.Kind == QueuePresentationKind.UpNext ? captured.SourceIndex : null);
            UpNextContainer.Children.Add(btn);
        }

        if (upcoming.Count == 0)
        {
            UpNextContainer.Children.Add(new TextBlock
            {
                Text = L10n.Tr("No upcoming tracks", "没有即将播放的曲目"),
                FontSize = 12,
                Foreground = ThemeBrushes.WhiteOverlay59,
                Margin = new Thickness(4, 6)
            });
        }

        var histList = QueuePresentation.BuildHistory(queue, take: 10);
        foreach (var row in histList)
        {
            var captured = row;
            var btn = BuildQueueRow(captured.Item.Track, () =>
            {
                _vm.Playback.PlayTrack(captured.Item.Track, [captured.Item.Track], captured.Item.FromContext);
            }, null, upNextIndex: null);
            HistoryContainer.Children.Add(btn);
        }

        if (histList.Count == 0)
        {
            HistoryContainer.Children.Add(new TextBlock
            {
                Text = L10n.Tr("No recent tracks", "没有最近曲目"),
                FontSize = 12,
                Foreground = ThemeBrushes.WhiteOverlay59,
                Margin = new Thickness(4, 6)
            });
        }
    }

    private void PlayKeepingCollectionContext(QueuePresentationRow row)
    {
        if (_vm?.Playback is null) return;
        var queue = _vm.Playback.Queue;
        // Always prefer the live collection Items as play context so tapping a
        // drawer row does not invent a one-track queue that drops the album/playlist.
        var context = queue.Items.Select(i => i.Track).ToList();
        if (context.Count == 0 || context.TrueForAll(t => t.Id != row.Item.Track.Id))
            context = [row.Item.Track, ..context];
        var from = row.Item.FromContext != QueueSource.Songs
            ? row.Item.FromContext
            : (queue.Current()?.FromContext ?? row.Item.FromContext);
        _vm.Playback.PlayTrack(row.Item.Track, context, from);
    }

    private Button BuildQueueRow(TrackSnapshot track, Action onPlay, Action? onRemove, int? upNextIndex)
    {
        var btn = new Button
        {
            Classes = { "queueRow" },
            Tag = upNextIndex
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var art = new ArtworkControl
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(4),
            GlyphSize = 14,
            SourceUrl = track.ArtworkUrl
        };
        Grid.SetColumn(art, 0);
        grid.Children.Add(art);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
            Spacing = 1
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = track.Title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = track.Artist,
            FontSize = 10,
            Foreground = ThemeBrushes.TextSecondaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        if (onRemove is not null)
        {
            var removeBtn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse("M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"),
                    Width = 10,
                    Height = 10,
                    Foreground = ThemeBrushes.WhiteOverlay66
                }
            };
            ToolTip.SetTip(removeBtn, L10n.Tr("Remove", "移除"));
            removeBtn.Click += (_, e) =>
            {
                e.Handled = true;
                onRemove();
            };
            Grid.SetColumn(removeBtn, 2);
            grid.Children.Add(removeBtn);
        }

        btn.Content = grid;
        btn.Click += (_, _) => onPlay();

        if (upNextIndex is int fromIdx)
        {
            btn.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
                _dragStart = e.GetPosition(this);
                _dragFromUpNext = fromIdx;
            };
            btn.PointerReleased += (_, e) =>
            {
                if (_dragStart is null || _dragFromUpNext < 0)
                {
                    _dragStart = null;
                    _dragFromUpNext = -1;
                    return;
                }

                var y = e.GetPosition(UpNextContainer).Y;
                var rowH = 44.0;
                var count = _vm?.Playback?.Queue.UpNext.Count ?? 0;
                if (count <= 0)
                {
                    _dragStart = null;
                    _dragFromUpNext = -1;
                    return;
                }

                var to = (int)Math.Clamp(Math.Floor(y / rowH), 0, count - 1);
                if (to != _dragFromUpNext)
                {
                    _vm?.MoveUpNextRow(_dragFromUpNext, to);
                    Dispatcher.UIThread.Post(UpdateQueue);
                    e.Handled = true;
                }

                _dragStart = null;
                _dragFromUpNext = -1;
            };
        }

        return btn;
    }
}
