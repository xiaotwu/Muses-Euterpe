using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Muses.App.Controls;

public partial class ArtistCardControl : UserControl
{
    public static readonly StyledProperty<string> ArtistNameProperty =
        AvaloniaProperty.Register<ArtistCardControl, string>(nameof(ArtistName), string.Empty);

    public static readonly StyledProperty<string> ArtistDetailProperty =
        AvaloniaProperty.Register<ArtistCardControl, string>(nameof(ArtistDetail), string.Empty);

    public static readonly StyledProperty<string?> ArtworkUrlProperty =
        AvaloniaProperty.Register<ArtistCardControl, string?>(nameof(ArtworkUrl));

    public static readonly StyledProperty<bool> IsNowPlayingProperty =
        AvaloniaProperty.Register<ArtistCardControl, bool>(nameof(IsNowPlaying), false);

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<ArtistCardControl, double>(nameof(CardWidth), 200);

    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<ArtistCardControl, double>(nameof(CardHeight), 264);

    public string ArtistName
    {
        get => GetValue(ArtistNameProperty);
        set => SetValue(ArtistNameProperty, value);
    }

    public string ArtistDetail
    {
        get => GetValue(ArtistDetailProperty);
        set => SetValue(ArtistDetailProperty, value);
    }

    public string? ArtworkUrl
    {
        get => GetValue(ArtworkUrlProperty);
        set => SetValue(ArtworkUrlProperty, value);
    }

    public bool IsNowPlaying
    {
        get => GetValue(IsNowPlayingProperty);
        set => SetValue(IsNowPlayingProperty, value);
    }

    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? CardSelected;

    public ArtistCardControl()
    {
        InitializeComponent();
        PlayPill.PointerPressed += OnPlayPillPressed;
        PointerReleased += OnCardPointerReleased;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (CardBorder is null) return;

        CardBorder.Width = CardWidth;
        CardBorder.Height = CardHeight;
        ArtistArt.SourceUrl = ArtworkUrl;
        ArtistNameText.Text = ArtistName;
        ArtistDetailText.Text = ArtistDetail;
        PlayPillText.Text = IsNowPlaying ? "Playing" : "Play";
        PlayPill.Classes.Set("playing", IsNowPlaying);
    }

    private void OnPlayPillPressed(object? sender, PointerPressedEventArgs e)
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
