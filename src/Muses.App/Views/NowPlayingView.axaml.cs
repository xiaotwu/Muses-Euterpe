using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Muses.App.Controls.Playback;
using Muses.App.Theme;
using Muses.App.ViewModels;
using Muses.Core.Lyrics;
using Muses.Core.NowPlaying;

namespace Muses.App.Views;

public partial class NowPlayingView : UserControl
{
    private const double VinylVerticalOffset = -12;
    private ShellViewModel? _vm;
    private IReadOnlyList<LyricLine>? _displayedLines;
    private readonly List<TextBlock> _lyricsBlocks = [];
    private int _activeLyricIndex = -1;
    private static readonly TransformOperations PlayingScale = TransformOperations.Parse(
        FormattableString.Invariant($"scale({NowPlayingLayout.LiveCoverPlayingScale})"));
    private Guid? _artworkTrack;
    private bool _coverInTransfer;
    private bool _presentationActive;
    private bool _showLyrics;
    private bool _updatingSlider;
    private IPointer? _seekPointer;
    private Guid? _seekTrack;

    public NowPlayingView()
    {
        InitializeComponent();
        ArtworkSlot.RenderTransform = TransformOperations.Identity;
        VinylContainer.RenderTransform = new TranslateTransform(0, VinylVerticalOffset);
        SetArtworkOpacities(1, 0);

        DataContextChanged += (_, _) => ConnectViewModel();
        SizeChanged += (_, _) => UpdateLayoutMode();
        StageScroll.SizeChanged += (_, _) => UpdateLayoutMode();
        KeyDown += OnUserControlKeyDown;
        ProgressSlider.AddHandler(PointerPressedEvent, OnSeekPressed, RoutingStrategies.Tunnel);
        ProgressSlider.AddHandler(PointerReleasedEvent, OnSeekReleased, RoutingStrategies.Tunnel);
        ProgressSlider.PointerCaptureLost += OnSeekCaptureLost;
        ProgressSlider.ValueChanged += OnSeekValueChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ConnectViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisconnectViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || VinylArtwork is null) return;

        if (IsVisible)
        {
            UpdateView();
        }
        else
        {
            VinylArtwork.SetPlaybackState(_artworkTrack, false);
            SetArtworkOpacities(1, 0);
        }
    }

    private void ConnectViewModel()
    {
        DisconnectViewModel();
        if (!this.IsAttachedToVisualTree()) return;

        _vm = DataContext as ShellViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            if (_vm.Lyrics is not null)
                _vm.Lyrics.Changed += OnLyricsChanged;
        }
        UpdateView();
    }

    private void DisconnectViewModel()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            if (_vm.Lyrics is not null)
                _vm.Lyrics.Changed -= OnLyricsChanged;
        }
        _vm = null;
        CancelSeek();
        VinylArtwork.SetPlaybackState(_artworkTrack, false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ProgressFraction))
            Dispatcher.UIThread.Post(UpdateTiming);
        else if (e.PropertyName == nameof(ShellViewModel.NowPlayingLyricsMode))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm?.NowPlayingLyricsMode == NowPlayingLyricsMode.Fullscreen)
                    InlineLyricsButton.IsChecked = _showLyrics = true;
                UpdateView();
            });
        }
        else if (e.PropertyName is nameof(ShellViewModel.NowPlayingMode)
                 or nameof(ShellViewModel.IsPlaying)
                 or nameof(ShellViewModel.NowPlayingYouTubeId)
                 or nameof(ShellViewModel.HasTrack)
                 || (e.PropertyName == nameof(ShellViewModel.NotPlayingLabel)
                     && _vm?.Playback?.State.Track?.Id != _artworkTrack))
            Dispatcher.UIThread.Post(UpdateView);
    }

    private void OnUserControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _vm is null) return;
        if (_vm.NowPlayingLyricsMode == NowPlayingLyricsMode.Fullscreen)
            _vm.ToggleNowPlayingLyricsMode();
        else
            _vm.CloseNowPlaying();
        e.Handled = true;
    }

    private void ToggleInlineLyrics(object? sender, RoutedEventArgs e)
    {
        _showLyrics = InlineLyricsButton.IsChecked == true;
        UpdateLayoutMode();
        if (_showLyrics) UpdateLyrics();
    }

    public void UpdateView()
    {
        if (_vm is null || !IsVisible) return;

        UpdateLayoutMode();
        UpdateArtworkMode();
        UpdateLyrics();
        UpdateTiming();
    }

    private bool IsVinyl => _vm?.NowPlayingMode == NowPlayingMode.Vinyl;

    internal bool IsArtworkArranged => IsArrangeValid && StageScroll.IsArrangeValid
        && ArtworkSlot.IsArrangeValid && (IsVinyl ? VinylContainer : CoverArtBorder).IsArrangeValid;

    internal (Rect Frame, double CornerRadius)? GetArtworkTarget(Visual relativeTo)
    {
        var artwork = IsVinyl ? VinylContainer : CoverArtBorder;
        if (CoverArtworkMorph.GetFrame(artwork, relativeTo) is not { } frame) return null;

        var radius = IsVinyl ? Math.Min(frame.Width, frame.Height) / 2 : 12 * frame.Width / artwork.Bounds.Width;
        return (frame, radius);
    }

    internal void SetCoverTransfer(bool transferring)
    {
        _coverInTransfer = transferring;
        SetArtworkOpacities(transferring || IsVinyl ? 0 : 1, transferring || !IsVinyl ? 0 : 1);
        if (transferring)
        {
            ArtworkSlot.Transitions = null;
            ArtworkSlot.RenderTransform = PlaybackArtworkScale();
            VinylArtwork.SetPlaybackState(_artworkTrack, false);
        }
        if (IsVisible) UpdateArtworkMode();
    }

    internal void SetPresentationActive(bool active)
    {
        if (active && !_presentationActive)
            InlineLyricsButton.IsChecked = _showLyrics = false;
        _presentationActive = active;
        if (!active)
            VinylArtwork.SetPlaybackState(_artworkTrack, false);
        else if (IsVisible)
            UpdateView();
    }

    internal void RefreshMotionPreference() => SetCoverTransfer(_coverInTransfer);

    private void UpdateArtworkMode()
    {
        if (_vm is null) return;

        _artworkTrack = _vm.Playback?.State.Track?.Id;
        if (_coverInTransfer) return;

        CoverArtBorder.Opacity = IsVinyl ? 0 : 1;
        VinylContainer.Opacity = IsVinyl ? 1 : 0;
        VinylArtwork.SetPlaybackState(_artworkTrack, IsVinyl && _presentationActive && _vm.IsPlaying
            && _vm.NowPlayingLyricsMode == NowPlayingLyricsMode.Inline);

        if (MotionChrome.PreferReducedMotion)
            ArtworkSlot.Transitions = null;
        else
            ArtworkSlot.Transitions ??= [new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(350),
                Easing = new SineEaseInOut(),
            }];
        ArtworkSlot.RenderTransform = PlaybackArtworkScale();
    }

    private TransformOperations PlaybackArtworkScale() =>
        _vm?.IsPlaying == true && !MotionChrome.PreferReducedMotion ? PlayingScale : TransformOperations.Identity;

    private void SetArtworkOpacities(double cover, double vinyl)
    {
        CoverArtBorder.Transitions = null;
        VinylContainer.Transitions = null;
        CoverArtBorder.Opacity = cover;
        VinylContainer.Opacity = vinyl;
        if (MotionChrome.PreferReducedMotion) return;

        CoverArtBorder.Transitions = [CreateArtworkFade()];
        VinylContainer.Transitions = [CreateArtworkFade()];
    }

    private static DoubleTransition CreateArtworkFade() => new()
    {
        Property = OpacityProperty,
        Duration = MotionChrome.OverlayDuration,
        Easing = new SineEaseInOut(),
    };

    private void UpdateLayoutMode()
    {
        if (_vm is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var fullscreen = _vm.NowPlayingLyricsMode == NowPlayingLyricsMode.Fullscreen;
        var showLyrics = fullscreen || _showLyrics;
        var split = !fullscreen && showLyrics && Bounds.Width >= NowPlayingLayout.SplitBreakpoint;
        var stacked = !fullscreen && showLyrics && !split;
        var width = Math.Max(0, Bounds.Width - 48);
        var splitInset = split ? 24 : 0;
        var contentWidth = Math.Min(width - 2 * splitInset, 1520);
        var stageHeight = StageScroll.Bounds.Height > 0 ? StageScroll.Bounds.Height : Bounds.Height - 170;
        var stageLimit = split ? contentWidth * .43 : Math.Min(width, stacked ? 420 : 620);
        // Reserve room for the largest playing artwork, its vinyl offset, and the track identity.
        var vinylRoom = Math.Ceiling(-2 * VinylVerticalOffset * NowPlayingLayout.LiveCoverPlayingScale) + 2;
        var stageSide = Math.Min(stageLimit, Math.Max(144, stageHeight - 94 - vinylRoom));
        var side = stageSide / NowPlayingLayout.LiveCoverPlayingScale;

        CoverArtBorder.Width = CoverArtBorder.Height = side;
        VinylContainer.Width = VinylContainer.Height = side;
        VinylContainer.CornerRadius = new CornerRadius(side / 2);
        ArtworkSlot.Width = ArtworkStage.Width = stageSide;
        ArtworkSlot.Height = stageSide + vinylRoom;
        ArtworkStage.IsVisible = !fullscreen;
        PlaybackDock.Width = Math.Min(width, 668);
        PlaybackDock.IsVisible = !fullscreen && _vm.HasTrack;
        LyricsColumn.IsVisible = showLyrics;
        LyricsColumn.MaxWidth = fullscreen ? 820 : double.PositiveInfinity;
        LyricsColumn.Margin = new Thickness(split ? Math.Clamp(width * .055, 36, 96) : 0, stacked ? 22 : 0, 0, 0);
        StageScroll.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        LyricsScrollViewer.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        StageGrid.Margin = new Thickness(splitInset, 0);
        StageGrid.ColumnDefinitions = new ColumnDefinitions(split ? "Auto,*" : "*");
        StageGrid.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "*");
        Grid.SetColumn(LyricsColumn, split ? 1 : 0);
        Grid.SetRow(LyricsColumn, stacked ? 1 : 0);
    }

    private void OnLyricsChanged() => Dispatcher.UIThread.Post(UpdateLyrics);

    private void UpdateLyrics()
    {
        if (!IsVisible || _vm?.Lyrics is not { } lyrics) return;

        if (!ReferenceEquals(_displayedLines, lyrics.Lines))
        {
            _displayedLines = lyrics.Lines;
            LyricsPanel.Children.Clear();
            _lyricsBlocks.Clear();
            _activeLyricIndex = -1;

            foreach (var line in lyrics.Lines)
            {
                var text = new TextBlock
                {
                    Text = line.Text,
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "lyricLine" }
                };
                _lyricsBlocks.Add(text);
                if (line.TimeMs is { } milliseconds)
                {
                    var button = new Button { Classes = { "nowPlayingLine" }, Content = text };
                    button.Click += (_, _) => _vm?.SeekToSeconds(milliseconds / 1000.0);
                    LyricsPanel.Children.Add(button);
                }
                else
                {
                    text.Margin = new Thickness(16, 12);
                    LyricsPanel.Children.Add(text);
                }
            }
        }

        LyricsPlaceholder.IsVisible = lyrics.Lines.Count == 0;
        var activeIndex = lyrics.CurrentLineIndex;
        if (_activeLyricIndex == activeIndex) return;

        if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricsBlocks.Count)
            _lyricsBlocks[_activeLyricIndex].Classes.Remove("active");

        _activeLyricIndex = activeIndex;
        if (activeIndex < 0 || activeIndex >= _lyricsBlocks.Count) return;

        _lyricsBlocks[activeIndex].Classes.Add("active");
        if (LyricsColumn.IsVisible && StageScroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
            LyricsPanel.Children[activeIndex].BringIntoView();
    }

    private void UpdateTiming()
    {
        if (_vm is null || !IsVisible || _seekPointer is not null) return;
        _updatingSlider = true;
        ProgressSlider.Maximum = Math.Max(0, _vm.CurrentDurationSeconds);
        ProgressSlider.IsEnabled = ProgressSlider.Maximum > 0;
        ProgressSlider.Value = _vm.CurrentPositionSeconds;
        _updatingSlider = false;
        SetTimingLabels(_vm.CurrentPositionSeconds);
    }

    private void OnSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_seekPointer is not null || _vm is not { CurrentDurationSeconds: > 0 }
            || !e.GetCurrentPoint(ProgressSlider).Properties.IsLeftButtonPressed) return;
        _seekPointer = e.Pointer;
        _seekTrack = _vm.Playback?.State.Track?.Id;
    }

    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _seekPointer) return;
        var trackId = _seekTrack;
        CancelSeek();
        if (_vm is { } vm && trackId == vm.Playback?.State.Track?.Id)
            vm.SeekToSeconds(ProgressSlider.Value);
        UpdateTiming();
    }

    private void OnSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer != _seekPointer) return;
        CancelSeek();
        UpdateTiming();
    }

    private void CancelSeek()
    {
        _seekPointer = null;
        _seekTrack = null;
    }

    private void OnSeekValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || _vm is not { CurrentDurationSeconds: > 0 } vm) return;
        if (_seekPointer is null)
            vm.SeekToSeconds(e.NewValue);
        SetTimingLabels(e.NewValue);
    }

    private void SetTimingLabels(double position)
    {
        ElapsedLabel.Text = FormatTime(position);
        RemainingLabel.Text = $"−{FormatTime((_vm?.CurrentDurationSeconds ?? 0) - position)}";
    }

    private static string FormatTime(double seconds)
    {
        var wholeSeconds = (int)Math.Max(0, seconds);
        return $"{wholeSeconds / 60}:{wholeSeconds % 60:D2}";
    }
}
