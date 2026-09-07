using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Muses.App.ViewModels;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class EQView : UserControl
{
    public EQView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RedrawCurve();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ShellViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ShellViewModel.EQBandViewModels))
                {
                    RedrawCurve();
                }
            };
        }
    }

    public void RedrawCurve()
    {
        var canvas = this.FindControl<Canvas>("CurveCanvas");
        if (canvas == null) return;
        canvas.Children.Clear();

        var width = canvas.Bounds.Width;
        var height = canvas.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var midY = height / 2.0;

        // 0dB Center line
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0, midY),
            EndPoint = new Point(width, midY),
            Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            StrokeThickness = 1
        });

        // ±12dB reference lines
        var top12Y = midY - (12.0 / 24.0) * midY;
        var bot12Y = midY + (12.0 / 24.0) * midY;
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0, top12Y),
            EndPoint = new Point(width, top12Y),
            Stroke = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            StrokeThickness = 1
        });
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0, bot12Y),
            EndPoint = new Point(width, bot12Y),
            Stroke = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            StrokeThickness = 1
        });

        if (DataContext is not ShellViewModel vm || vm.EQBandViewModels.Count == 0) return;

        var bands = vm.EQBandViewModels;
        var n = bands.Count;
        var stepX = width / Math.Max(1, n - 1);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < n; i++)
            {
                var x = i * stepX;
                var gain = bands[i].Gain;
                var y = midY - (gain / 24.0) * midY;

                if (i == 0)
                    ctx.BeginFigure(new Point(x, y), false);
                else
                    ctx.LineTo(new Point(x, y));
            }
        }

        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(ThemeBrushes.AccentColor),
            StrokeThickness = 2
        });

        // Draw points
        for (int i = 0; i < n; i++)
        {
            var x = i * stepX;
            var gain = bands[i].Gain;
            var y = midY - (gain / 24.0) * midY;

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(ThemeBrushes.AccentColor)
            };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            canvas.Children.Add(dot);
        }
    }
}
