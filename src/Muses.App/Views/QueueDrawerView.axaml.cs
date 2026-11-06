using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private PlaybackService? _subscribedPlayback;
    private Point? _dragStart;
    private int _dragFromUpNext = -1;

    public QueueDrawerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnQueueKeyDown, RoutingStrategies.Tunnel);
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
        {
            UpdateQueue();
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible && !IsKeyboardFocusWithin)
                    CloseQueueButton.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        }
    }

    private void ApplyLocalizedChrome()
    {
        QueueTitle.Text = L10n.Tr("Playing Next", "接下来播放");
        NowPlayingHeader.Text = L10n.Tr("Now Playing", "正在播放");
        HistoryHeader.Text = L10n.Tr("History", "历史记录");
        ClearLabel.Text = L10n.Tr("Clear", "清空");
    }

    private void Subscribe()
    {
        if (_vm is null) return;
        _vm.PropertyChanged -= OnShellChanged;
        _vm.PropertyChanged += OnShellChanged;
        ConnectPlayback();
    }

    private void Unsubscribe()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnShellChanged;
        if (_subscribedPlayback is not null)
            _subscribedPlayback.EventBus.EventPosted -= OnPlaybackEvent;
        _subscribedPlayback = null;
    }

    private void ConnectPlayback()
    {
        if (ReferenceEquals(_subscribedPlayback, _vm?.Playback)) return;
        if (_subscribedPlayback is not null)
            _subscribedPlayback.EventBus.EventPosted -= OnPlaybackEvent;
        _subscribedPlayback = _vm?.Playback;
        if (_subscribedPlayback is not null)
            _subscribedPlayback.EventBus.EventPosted += OnPlaybackEvent;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Playback) or nameof(ShellViewModel.ShuffleOn))
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConnectPlayback();
                UpdateQueue();
            });
        }
    }

    private void OnPlaybackEvent(PlaybackEvent e)
    {
        if (e.Kind is PlaybackEventKind.TrackStarted or PlaybackEventKind.TrackCompleted
            or PlaybackEventKind.TrackSkipped or PlaybackEventKind.TrackStopped or PlaybackEventKind.QueueChanged)
            Dispatcher.UIThread.Post(UpdateQueue);
    }

    private IEnumerable<Button> UpcomingButtons() =>
        UpNextContainer.Children.Concat(CollectionContainer.Children).OfType<Button>();

    private void OnQueueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
            return;

        var rows = UpcomingButtons().Prepend(NowPlayingQueueButton)
            .Where(row => row.IsEffectivelyVisible && row.IsEffectivelyEnabled).ToList();
        if (rows.Count == 0) return;

        var focused = rows.FindIndex(row => row.IsKeyboardFocusWithin);
        var target = e.Key switch
        {
            Key.Home => 0,
            Key.End => rows.Count - 1,
            Key.Up => focused < 0 ? rows.Count - 1 : Math.Max(0, focused - 1),
            _ => Math.Min(rows.Count - 1, focused + 1),
        };
        if (rows[target].Focus(NavigationMethod.Directional))
        {
            rows[target].BringIntoView();
            e.Handled = true;
        }
    }

    public void UpdateQueue()
    {
        if (_vm?.Playback is null || !IsVisible) return;
        var queue = _vm.Playback.Queue;
        var previousRows = UpcomingButtons().ToList();
        var focusedIndex = previousRows.FindIndex(row => row.IsKeyboardFocusWithin);
        var focusedRow = focusedIndex >= 0 ? previousRows[focusedIndex].Tag as QueuePresentationRow : null;

        UpNextContainer.Children.Clear();
        CollectionContainer.Children.Clear();
        HistoryContainer.Children.Clear();

        var upcoming = QueuePresentation.BuildUpcoming(queue);
        UpNextHeader.Text = $"{L10n.Tr("Up Next", "下一首")} ({queue.UpNext.Count})";
        CollectionHeader.Text = $"{L10n.Tr("From Current Collection", "来自当前集合")} ({upcoming.Count - queue.UpNext.Count})";

        foreach (var row in upcoming)
        {
            var captured = row;
            var btn = BuildQueueRow(
                captured.Item.Track,
                onPlay: () => _vm?.Playback?.PlayQueueItem(captured.Item.Id),
                onRemove: () =>
                {
                    var items = captured.Kind == QueuePresentationKind.UpNext ? queue.UpNext : queue.Items;
                    var index = items.FindIndex(item => item.Id == captured.Item.Id);
                    if (index >= 0)
                        _vm.RemoveUpcomingRow(captured with { SourceIndex = index });
                },
                queueRow: captured);
            var section = captured.Kind == QueuePresentationKind.UpNext ? UpNextContainer : CollectionContainer;
            section.Children.Add(btn);
        }

        if (queue.UpNext.Count == 0)
        {
            UpNextContainer.Children.Add(new TextBlock
            {
                Text = L10n.Tr("Choose Play Next from any track menu", "在曲目菜单中选择「下一首播放」"),
                FontSize = 12,
                Foreground = ThemeBrushes.TextSecondaryBrush,
                Margin = new Thickness(4, 6)
            });
        }

        if (CollectionContainer.Children.Count == 0)
        {
            CollectionContainer.Children.Add(new TextBlock
            {
                Text = L10n.Tr("No more tracks in the collection", "当前集合没有剩余曲目"),
                FontSize = 12,
                Foreground = ThemeBrushes.TextSecondaryBrush,
                Margin = new Thickness(4, 6)
            });
        }

        var histList = queue.History.Take(10).ToArray();
        foreach (var item in histList)
        {
            var btn = BuildQueueRow(item.Track, () =>
            {
                _vm.Playback.PlayTrack(item.Track, [item.Track], item.FromContext);
            }, null);
            HistoryContainer.Children.Add(btn);
        }

        if (histList.Length == 0)
        {
            HistoryContainer.Children.Add(new TextBlock
            {
                Text = L10n.Tr("No recent tracks", "没有最近曲目"),
                FontSize = 12,
                Foreground = ThemeBrushes.TextSecondaryBrush,
                Margin = new Thickness(4, 6)
            });
        }

        if (focusedRow is not null)
        {
            var rows = UpcomingButtons().ToList();
            var target = rows.FirstOrDefault(row => row.Tag is QueuePresentationRow entry
                && entry.Item.Id == focusedRow.Item.Id);
            if (target is null && queue.Current()?.Id == focusedRow.Item.Id && NowPlayingQueueButton.IsEffectivelyVisible)
                target = NowPlayingQueueButton;
            target ??= rows.Count > 0 ? rows[Math.Min(focusedIndex, rows.Count - 1)] : CloseQueueButton;
            target.Focus(NavigationMethod.Directional);
        }
    }

    private Button BuildQueueRow(TrackSnapshot track, Action onPlay, Action? onRemove, QueuePresentationRow? queueRow = null)
    {
        var btn = new Button
        {
            Classes = { "queueRow" },
            Tag = queueRow
        };
        AutomationProperties.SetName(btn, track.Title);

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
            AutomationProperties.SetName(removeBtn, L10n.Tr("Remove from queue", "从队列移除"));
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

        if (queueRow is { Kind: QueuePresentationKind.UpNext, SourceIndex: var fromIdx })
        {
            btn.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
                _dragStart = e.GetPosition(this);
                _dragFromUpNext = fromIdx;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            btn.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (_dragStart is null || _dragFromUpNext < 0)
                {
                    _dragStart = null;
                    _dragFromUpNext = -1;
                    return;
                }

                var start = _dragStart.Value;
                var from = _dragFromUpNext;
                _dragStart = null;
                _dragFromUpNext = -1;
                if (Math.Abs(e.GetPosition(this).Y - start.Y) < 5) return;

                var y = e.GetPosition(UpNextContainer).Y;
                var target = UpNextContainer.Children.OfType<Button>()
                    .Where(row => row.Tag is QueuePresentationRow)
                    .MinBy(row => Math.Abs(y - row.Bounds.Center.Y));
                if (target?.Tag is not QueuePresentationRow { SourceIndex: var to } || to == from) return;

                _vm?.MoveUpNextRow(from, to);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        return btn;
    }
}
