using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Muses.Core.History;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class ListeningHeatmapView : UserControl
{
    private static readonly IBrush BrushNone = ThemeBrushes.WhiteOverlay0A;
    private static readonly IBrush BrushTrace = ThemeBrushes.AccentOverlay26;
    private static readonly IBrush BrushLow = ThemeBrushes.AccentOverlay59;
    private static readonly IBrush BrushMedium = ThemeBrushes.AccentOverlay99;
    private static readonly IBrush BrushHigh = ThemeBrushes.AccentOverlayD9;
    private static readonly IBrush BrushPeak = ThemeBrushes.Accent;

    public ListeningHeatmapView()
    {
        InitializeComponent();
        BuildHoursHeader();
    }

    private void BuildHoursHeader()
    {
        HoursHeaderGrid.ColumnDefinitions.Clear();
        HoursHeaderGrid.Children.Clear();

        for (var h = 0; h < 24; h++)
        {
            HoursHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
            if (h % 3 == 0)
            {
                var tb = new TextBlock
                {
                    Text = $"{h}",
                    FontSize = 10,
                    Foreground = ThemeBrushes.WhiteOverlay73,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(tb, h);
                HoursHeaderGrid.Children.Add(tb);
            }
        }
    }

    public void RenderHeatmap(ListeningHeatmap heatmap)
    {
        // 1. Summaries
        var totalHours = heatmap.TotalMs / 3600000.0;
        TotalListenedLabel.Text = $"{totalHours:F1} hours";

        if (heatmap.PeakCell is not null)
        {
            var peakRow = heatmap.Rows.FirstOrDefault(r => r.Index == heatmap.PeakCell.RowIndex);
            var label = peakRow?.Label ?? "";
            PeakIntervalLabel.Text = $"{label} {heatmap.PeakCell.Hour}:00 ({heatmap.PeakCell.TotalMs / 60000}m)";
        }
        else
        {
            PeakIntervalLabel.Text = "—";
        }

        var mostActiveRow = heatmap.Rows.OrderByDescending(r => r.Cells.Sum(c => c.TotalMs)).FirstOrDefault(r => r.Cells.Sum(c => c.TotalMs) > 0);
        ActiveDayLabel.Text = mostActiveRow?.Label ?? "—";

        // 2. Rows & Cells
        RowsContainer.Children.Clear();
        foreach (var row in heatmap.Rows)
        {
            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("44")
            };

            var dayLabel = new TextBlock
            {
                Text = row.Label,
                FontSize = 11,
                Foreground = ThemeBrushes.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dayLabel, 0);
            rowGrid.Children.Add(dayLabel);

            var cellsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2
            };

            foreach (var cell in row.Cells)
            {
                var brush = cell.Level switch
                {
                    ListeningHeatmapLevel.Trace => BrushTrace,
                    ListeningHeatmapLevel.Low => BrushLow,
                    ListeningHeatmapLevel.Medium => BrushMedium,
                    ListeningHeatmapLevel.High => BrushHigh,
                    ListeningHeatmapLevel.Peak => BrushPeak,
                    _ => BrushNone
                };

                var cellBorder = new Border
                {
                    Width = 26,
                    Height = 20,
                    CornerRadius = new Avalonia.CornerRadius(3),
                    Background = brush,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };

                var mins = cell.TotalMs / 60000;
                ToolTip.SetTip(cellBorder, $"{row.Label} at {cell.Hour:D2}:00\n{mins} min listened • {cell.EventCount} tracks played");

                cellsStack.Children.Add(cellBorder);
            }

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(cellsStack, 1);
            rowGrid.Children.Add(cellsStack);

            RowsContainer.Children.Add(rowGrid);
        }
    }
}
