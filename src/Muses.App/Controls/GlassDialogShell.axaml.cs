using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Muses.App.Controls;

/// <summary>
/// Centered glass dialog panel chrome (fill, hairline, corner radius, optional title).
/// Scrim/overlay hosting stays at the call site so each modal keeps its own dismiss handler.
/// </summary>
public partial class GlassDialogShell : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<GlassDialogShell, string?>(nameof(Title));

    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<GlassDialogShell, double>(nameof(TitleFontSize), 20d);

    public static readonly StyledProperty<object?> DialogContentProperty =
        AvaloniaProperty.Register<GlassDialogShell, object?>(nameof(DialogContent));

    public GlassDialogShell()
    {
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(24);
        InitializeComponent();
        ApplyChrome();
        SyncTitle();
        SyncBody();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>Dialog body. Prefer property-element form so it does not replace UserControl.Content.</summary>
    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty || change.Property == TitleFontSizeProperty)
            SyncTitle();
        else if (change.Property == DialogContentProperty)
            SyncBody();
        else if (change.Property == CornerRadiusProperty
                 || change.Property == PaddingProperty
                 || change.Property == ClipToBoundsProperty
                 || change.Property == BackgroundProperty
                 || change.Property == BorderBrushProperty
                 || change.Property == BorderThicknessProperty)
            ApplyChrome();
    }

    private void ApplyChrome()
    {
        if (ShellBorder is null)
            return;

        ShellBorder.CornerRadius = CornerRadius;
        ShellBorder.Padding = Padding;
        ShellBorder.ClipToBounds = ClipToBounds;

        if (Background is not null)
            ShellBorder.Background = Background;
        if (BorderBrush is not null)
            ShellBorder.BorderBrush = BorderBrush;
        if (BorderThickness != default)
            ShellBorder.BorderThickness = BorderThickness;
    }

    private void SyncTitle()
    {
        if (TitleBlock is null)
            return;

        var title = Title;
        var hasTitle = !string.IsNullOrWhiteSpace(title);
        TitleBlock.Text = title ?? string.Empty;
        TitleBlock.FontSize = TitleFontSize;
        TitleBlock.IsVisible = hasTitle;
    }

    private void SyncBody()
    {
        if (BodyHost is null)
            return;

        BodyHost.Content = DialogContent;
    }
}
