using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.Queue;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class QueueDrawerView : UserControl
{
    private ShellViewModel? _vm;

    public QueueDrawerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm = DataContext as ShellViewModel;
        UpdateQueue();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateQueue();
    }

    public void UpdateQueue()
    {
        if (_vm?.Playback is null) return;
        var queue = _vm.Playback.Queue;

        UpNextContainer.Children.Clear();
        HistoryContainer.Children.Clear();

        // 1. Up Next items
        var upNextList = queue.UpNext.ToList();
        var remainingItems = queue.Items.Skip(queue.CurrentIndex + 1).ToList();
        var allUpcoming = upNextList.Concat(remainingItems).ToList();

        UpNextHeader.Text = $"Up Next ({allUpcoming.Count})";

        for (var i = 0; i < allUpcoming.Count; i++)
        {
            var item = allUpcoming[i];
            var btn = BuildQueueRow(item.Track, () =>
            {
                _vm.Playback.PlayTrack(item.Track, allUpcoming.Select(q => q.Track).ToList(), QueueSource.Playlist);
            }, () =>
            {
                _vm.RemoveQueueItem(i);
                Dispatcher.UIThread.Post(UpdateQueue);
            });
            UpNextContainer.Children.Add(btn);
        }

        if (allUpcoming.Count == 0)
        {
            UpNextContainer.Children.Add(new TextBlock
            {
                Text = "No upcoming tracks",
                FontSize = 12,
                Foreground = ThemeBrushes.WhiteOverlay59,
                Margin = new Avalonia.Thickness(4, 6)
            });
        }

        // 2. History items
        var histList = queue.History.TakeLast(10).Reverse().ToList();
        foreach (var item in histList)
        {
            var btn = BuildQueueRow(item.Track, () =>
            {
                _vm.Playback.PlayTrack(item.Track, [item.Track], QueueSource.Album);
            }, null);
            HistoryContainer.Children.Add(btn);
        }

        if (histList.Count == 0)
        {
            HistoryContainer.Children.Add(new TextBlock
            {
                Text = "No recent tracks",
                FontSize = 12,
                Foreground = ThemeBrushes.WhiteOverlay59,
                Margin = new Avalonia.Thickness(4, 6)
            });
        }
    }

    private Button BuildQueueRow(TrackSnapshot track, Action onPlay, Action? onRemove)
    {
        var btn = new Button
        {
            Classes = { "queueRow" },
            Tag = track
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var art = new ArtworkControl
        {
            Width = 32,
            Height = 32,
            CornerRadius = new Avalonia.CornerRadius(4),
            GlyphSize = 14,
            SourceUrl = track.ArtworkUrl
        };
        Grid.SetColumn(art, 0);
        grid.Children.Add(art);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(10, 0, 8, 0),
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
                BorderThickness = new Avalonia.Thickness(0),
                Width = 24,
                Height = 24,
                Padding = new Avalonia.Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse("M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"),
                    Width = 10,
                    Height = 10,
                    Foreground = ThemeBrushes.WhiteOverlay66
                }
            };
            removeBtn.Click += (s, e) =>
            {
                e.Handled = true;
                onRemove();
            };
            Grid.SetColumn(removeBtn, 2);
            grid.Children.Add(removeBtn);
        }

        btn.Content = grid;
        btn.Click += (_, _) => onPlay();
        return btn;
    }
}
