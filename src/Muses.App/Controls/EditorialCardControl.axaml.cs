using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Muses.App.Controls;

public partial class EditorialCardControl : UserControl
{
    public static readonly StyledProperty<string> EyebrowProperty =
        AvaloniaProperty.Register<EditorialCardControl, string>(nameof(Eyebrow), "FEATURED");

    public static readonly StyledProperty<string> CardTitleProperty =
        AvaloniaProperty.Register<EditorialCardControl, string>(nameof(CardTitle), string.Empty);

    public static readonly StyledProperty<string> CardSubtitleProperty =
        AvaloniaProperty.Register<EditorialCardControl, string>(nameof(CardSubtitle), string.Empty);

    public static readonly StyledProperty<string?> ArtworkUrlProperty =
        AvaloniaProperty.Register<EditorialCardControl, string?>(nameof(ArtworkUrl));

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<EditorialCardControl, double>(nameof(CardWidth), 540);

    public static readonly StyledProperty<double> ImageHeightProperty =
        AvaloniaProperty.Register<EditorialCardControl, double>(nameof(ImageHeight), 309);

    public string Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string CardTitle
    {
        get => GetValue(CardTitleProperty);
        set => SetValue(CardTitleProperty, value);
    }

    public string CardSubtitle
    {
        get => GetValue(CardSubtitleProperty);
        set => SetValue(CardSubtitleProperty, value);
    }

    public string? ArtworkUrl
    {
        get => GetValue(ArtworkUrlProperty);
        set => SetValue(ArtworkUrlProperty, value);
    }

    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double ImageHeight
    {
        get => GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? CardSelected;

    public EditorialCardControl()
    {
        InitializeComponent();
        HoverPlay.Click += OnHoverPlayClicked;
        PointerReleased += OnCardPointerReleased;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (EyebrowText is null) return;

        Width = CardWidth;
        EyebrowText.Text = (Eyebrow ?? "FEATURED").ToUpperInvariant();
        TitleText.Text = CardTitle;
        SubtitleText.Text = CardSubtitle;
        ArtBorder.Width = CardWidth;
        ArtBorder.Height = ImageHeight;
        Artwork.SourceUrl = ArtworkUrl;
    }

    private void OnHoverPlayClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PlayRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            CardSelected?.Invoke(this, EventArgs.Empty);
        }
    }
}
