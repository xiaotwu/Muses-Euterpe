using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;

namespace Muses.App.Controls;

public partial class YouTubeMark : UserControl
{
    public static readonly StyledProperty<double> MarkSizeProperty =
        AvaloniaProperty.Register<YouTubeMark, double>(nameof(MarkSize), 13);

    public YouTubeMark()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, "YouTube");
    }

    public double MarkSize
    {
        get => GetValue(MarkSizeProperty);
        set => SetValue(MarkSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkSizeProperty && Root is not null)
        {
            Root.Width = MarkSize * 1.28;
            Root.Height = MarkSize * 0.92;
            Plate.CornerRadius = new CornerRadius(MarkSize * 0.22);
            Play.Width = MarkSize * 0.42;
            Play.Height = MarkSize * 0.42;
        }
    }
}
