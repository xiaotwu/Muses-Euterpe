using Avalonia;
using Avalonia.Controls;

namespace Muses.App.Views;

public partial class PlaceholderPage : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PlaceholderPage, string>(nameof(Title), "Muses");

    public PlaceholderPage()
    {
        InitializeComponent();
        TitleBlock.Text = Title;
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty && TitleBlock is not null)
            TitleBlock.Text = Title;
    }
}
