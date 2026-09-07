using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.History;
using Muses.Core.Queue;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class HistoryView : UserControl
{
    private ShellViewModel? _vm;
    private RecapRange _range = RecapRange.Week;

    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                RefreshDashboard();
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm?.History is not null)
        {
            _vm.History.Changed -= OnHistoryChanged;
        }

        _vm = DataContext as ShellViewModel;
        if (_vm?.History is not null)
        {
            _vm.History.Changed += OnHistoryChanged;
            RefreshDashboard();
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshDashboard();
    }

    private void OnHistoryChanged()
    {
        Dispatcher.UIThread.Post(RefreshDashboard);
    }

    public void RefreshDashboard()
    {
        if (_vm?.History is null) return;
        var dash = _vm.History.GetDashboard(_range);

        // 1. KPI Cards
        var hours = dash.TotalListenedMs / 3600000.0;
        TotalTimeKpi.Text = $"{hours:F1} hours";
        EventsCountKpi.Text = $"{dash.TotalEventCount} plays recorded";

        var compPercent = dash.TotalEventCount > 0
            ? (int)Math.Round((double)dash.CompletedCount / dash.TotalEventCount * 100)
            : 0;
        CompletionKpi.Text = $"{compPercent}%";
        CompletionDetailKpi.Text = $"{dash.CompletedCount} completed • {dash.SkippedCount} skipped";

        VarietyKpi.Text = $"{dash.UniqueTracks} tracks";
        VarietyArtistsKpi.Text = $"across {dash.UniqueArtists} artists";

        // 2. Heatmap
        HeatmapControl.RenderHeatmap(dash.Heatmap);

        // 3. Top Tracks
        TopTracksContainer.Children.Clear();
        foreach (var track in dash.TopTracks)
        {
            TopTracksContainer.Children.Add(BuildTrackRow(track));
        }

        if (dash.TopTracks.Count == 0)
        {
            TopTracksContainer.Children.Add(new TextBlock
            {
                Text = "No top tracks in this range yet.",
                FontSize = 13,
                Foreground = ThemeBrushes.WhiteOverlay59,
                Margin = new Avalonia.Thickness(0, 10)
            });
        }
    }

    private Button BuildTrackRow(TrackTally tally)
    {
        var btn = new Button
        {
            Classes = { "historyTrackRow" },
            Tag = tally
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
            SourceUrl = tally.ArtworkUrl
        };
        Grid.SetColumn(art, 0);
        grid.Children.Add(art);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 0, 8, 0),
            Spacing = 2
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = tally.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = tally.Artist,
            FontSize = 11,
            Foreground = ThemeBrushes.TextSecondaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var statsPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };
        statsPanel.Children.Add(new TextBlock
        {
            Text = $"{tally.Count} plays",
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = ThemeBrushes.Accent
        });
        statsPanel.Children.Add(new TextBlock
        {
            Text = $"{tally.TotalMs / 60000}m",
            FontSize = 12,
            Foreground = ThemeBrushes.WhiteOverlay73
        });
        Grid.SetColumn(statsPanel, 2);
        grid.Children.Add(statsPanel);

        btn.Content = grid;
        btn.Click += (_, _) =>
        {
            if (_vm?.Library is not null && _vm.Playback is not null)
            {
                if (Guid.TryParse(tally.TrackId, out var g))
                {
                    var tr = _vm.Library.Get(g);
                    if (tr is not null)
                    {
                        var snap = tr.ToSnapshot();
                        _vm.Playback.PlayTrack(snap, [snap], QueueSource.Search);
                    }
                }
            }
        };
        return btn;
    }

    private void SetRange(RecapRange range)
    {
        _range = range;
        RangeWeekBtn.Classes.Set("selected", range == RecapRange.Week);
        RangeMonthBtn.Classes.Set("selected", range == RecapRange.Month);
        RangeYearBtn.Classes.Set("selected", range == RecapRange.Year);
        RangeAllTimeBtn.Classes.Set("selected", range == RecapRange.AllTime);
        RefreshDashboard();
    }

    private void OnRangeWeek(object? sender, RoutedEventArgs e) => SetRange(RecapRange.Week);
    private void OnRangeMonth(object? sender, RoutedEventArgs e) => SetRange(RecapRange.Month);
    private void OnRangeYear(object? sender, RoutedEventArgs e) => SetRange(RecapRange.Year);
    private void OnRangeAllTime(object? sender, RoutedEventArgs e) => SetRange(RecapRange.AllTime);

    private void OnClearHistoryClicked(object? sender, RoutedEventArgs e)
    {
        _vm?.History?.Clear();
    }
}
