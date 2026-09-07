using Avalonia;
using Avalonia.Controls;

namespace Muses.App.Controls;

public partial class MusesMark : UserControl
{
    public static readonly StyledProperty<double> MarkSizeProperty =
        AvaloniaProperty.Register<MusesMark, double>(nameof(MarkSize), 20);

    public MusesMark()
    {
        InitializeComponent();
    }

    public double MarkSize
    {
        get => GetValue(MarkSizeProperty);
        set => SetValue(MarkSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkSizeProperty && Host is not null)
        {
            Host.Width = MarkSize;
            Host.Height = MarkSize;
            Host.CornerRadius = new CornerRadius(Math.Max(4, MarkSize * 0.22));
        }
    }
}
