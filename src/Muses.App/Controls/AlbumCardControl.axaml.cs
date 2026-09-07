using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Muses.App.Controls;

public enum AlbumCardPresentationStyle
{
    Standard,
    Home,
    HeroCard
}

public partial class AlbumCardControl : UserControl
{
    public static readonly StyledProperty<string> CardTitleProperty =
        AvaloniaProperty.Register<AlbumCardControl, string>(nameof(CardTitle), string.Empty);

    public static readonly StyledProperty<string> CardSubtitleProperty =
        AvaloniaProperty.Register<AlbumCardControl, string>(nameof(CardSubtitle), string.Empty);

    public static readonly StyledProperty<string?> ArtworkUrlProperty =
        AvaloniaProperty.Register<AlbumCardControl, string?>(nameof(ArtworkUrl));

    public static readonly StyledProperty<bool> IsYouTubeProperty =
        AvaloniaProperty.Register<AlbumCardControl, bool>(nameof(IsYouTube), false);

    public static readonly StyledProperty<bool> IsNowPlayingProperty =
        AvaloniaProperty.Register<AlbumCardControl, bool>(nameof(IsNowPlaying), false);

    public static readonly StyledProperty<string?> TagTextProperty =
        AvaloniaProperty.Register<AlbumCardControl, string?>(nameof(TagText), "ALBUM");

    public static readonly StyledProperty<AlbumCardPresentationStyle> CardStyleProperty =
        AvaloniaProperty.Register<AlbumCardControl, AlbumCardPresentationStyle>(nameof(CardStyle), AlbumCardPresentationStyle.HeroCard);

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<AlbumCardControl, double>(nameof(CardWidth), 200);

    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<AlbumCardControl, double>(nameof(CardHeight), 264);

    public static readonly StyledProperty<double> ArtworkHeightProperty =
        AvaloniaProperty.Register<AlbumCardControl, double>(nameof(ArtworkHeight), 180);

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

    public bool IsYouTube
    {
        get => GetValue(IsYouTubeProperty);
        set => SetValue(IsYouTubeProperty, value);
    }

    public bool IsNowPlaying
    {
        get => GetValue(IsNowPlayingProperty);
        set => SetValue(IsNowPlayingProperty, value);
    }

    public string? TagText
    {
        get => GetValue(TagTextProperty);
        set => SetValue(TagTextProperty, value);
    }

    public AlbumCardPresentationStyle CardStyle
    {
        get => GetValue(CardStyleProperty);
        set => SetValue(CardStyleProperty, value);
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

    public double ArtworkHeight
    {
        get => GetValue(ArtworkHeightProperty);
        set => SetValue(ArtworkHeightProperty, value);
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? CardSelected;

    public AlbumCardControl()
    {
        InitializeComponent();
        StandardHoverPlay.Click += OnHoverPlayClicked;
        HomeHoverPlay.Click += OnHoverPlayClicked;
        HeroPlayPill.PointerPressed += OnHeroPlayPillPressed;
        PointerReleased += OnCardPointerReleased;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (StandardContainer is null) return;

        StandardContainer.IsVisible = CardStyle == AlbumCardPresentationStyle.Standard;
        HomeContainer.IsVisible = CardStyle == AlbumCardPresentationStyle.Home;
        HeroContainer.IsVisible = CardStyle == AlbumCardPresentationStyle.HeroCard;

        switch (CardStyle)
        {
            case AlbumCardPresentationStyle.Standard:
                StandardContainer.Width = CardWidth;
                StandardArtPanel.Width = CardWidth;
                StandardArtPanel.Height = ArtworkHeight > 0 ? ArtworkHeight : CardWidth;
                StandardArt.SourceUrl = ArtworkUrl;
                StandardTitle.Text = CardTitle;
                StandardSubtitle.Text = CardSubtitle;
                StandardYouTubeBadge.IsVisible = IsYouTube;
                break;

            case AlbumCardPresentationStyle.Home:
                HomeContainer.Width = CardWidth;
                HomeArtPanel.Width = CardWidth;
                HomeArtPanel.Height = ArtworkHeight > 0 ? ArtworkHeight : CardWidth;
                HomeArt.SourceUrl = ArtworkUrl;
                HomeTitle.Text = CardTitle;
                HomeSubtitle.Text = CardSubtitle;
                HomeYouTubeBadge.IsVisible = IsYouTube;
                break;

            case AlbumCardPresentationStyle.HeroCard:
                HeroContainer.Width = CardWidth;
                HeroContainer.Height = CardHeight;
                HeroArt.SourceUrl = ArtworkUrl;
                HeroTitle.Text = CardTitle;
                HeroSubtitle.Text = CardSubtitle;
                HeroYouTubeBadge.IsVisible = IsYouTube;
                HeroTagText.Text = TagText ?? "ALBUM";
                HeroTagBorder.IsVisible = !string.IsNullOrEmpty(TagText);
                HeroPlayText.Text = IsNowPlaying ? "Playing" : "Play";
                HeroPlayPill.Classes.Set("playing", IsNowPlaying);
                break;
        }
    }

    private void OnHoverPlayClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PlayRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeroPlayPillPressed(object? sender, PointerPressedEventArgs e)
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
