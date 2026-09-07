using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Muses.App.Theme;

namespace Muses.App.Controls;

public partial class CompactTrackRowControl : UserControl
{
    public static readonly StyledProperty<string> TrackTitleProperty =
        AvaloniaProperty.Register<CompactTrackRowControl, string>(nameof(TrackTitle), string.Empty);

    public static readonly StyledProperty<string> TrackArtistProperty =
        AvaloniaProperty.Register<CompactTrackRowControl, string>(nameof(TrackArtist), string.Empty);

    public static readonly StyledProperty<string?> ArtworkUrlProperty =
        AvaloniaProperty.Register<CompactTrackRowControl, string?>(nameof(ArtworkUrl));

    public static readonly StyledProperty<bool> IsNowPlayingProperty =
        AvaloniaProperty.Register<CompactTrackRowControl, bool>(nameof(IsNowPlaying), false);

    public string TrackTitle
    {
        get => GetValue(TrackTitleProperty);
        set => SetValue(TrackTitleProperty, value);
    }

    public string TrackArtist
    {
        get => GetValue(TrackArtistProperty);
        set => SetValue(TrackArtistProperty, value);
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

    public event EventHandler? PlayRequested;

    public CompactTrackRowControl()
    {
        InitializeComponent();
        PointerReleased += OnRowPointerReleased;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (TitleText is null) return;

        TrackArt.SourceUrl = ArtworkUrl;
        TitleText.Text = TrackTitle;
        ArtistText.Text = TrackArtist;
        NowPlayingIcon.IsVisible = IsNowPlaying;
        TitleText.Foreground = IsNowPlaying ? ThemeBrushes.Accent : ThemeBrushes.TextPrimaryBrush;
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            PlayRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
