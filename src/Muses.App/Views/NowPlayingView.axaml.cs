using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.ViewModels;
using Muses.Core.Lyrics;
using Muses.Core.NowPlaying;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class NowPlayingView : UserControl
{
    private ShellViewModel? _vm;
    private readonly List<TextBlock> _lyricsBlocks = [];
    private readonly DispatcherTimer _vinylTimer;
    private double _vinylAngle;
    private readonly RotateTransform _vinylRotation = new();

    public NowPlayingView()
    {
        InitializeComponent();
        VinylDisc.RenderTransform = _vinylRotation;

        _vinylTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _vinylTimer.Tick += OnVinylTick;

        DataContextChanged += OnDataContextChanged;
        KeyDown += OnUserControlKeyDown;
        SizeChanged += (_, _) => UpdateLayoutMode();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm?.Lyrics is not null)
        {
            _vm.Lyrics.Changed -= OnLyricsChanged;
        }

        _vm = DataContext as ShellViewModel;
        if (_vm?.Lyrics is not null)
        {
            _vm.Lyrics.Changed += OnLyricsChanged;
        }

        UpdateView();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                Focus();
                _vinylTimer.Start();
                UpdateView();
            }
            else
            {
                _vinylTimer.Stop();
            }
        }
    }

    private void OnUserControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm?.CloseNowPlaying();
            e.Handled = true;
        }
    }

    private void OnVinylTick(object? sender, EventArgs e)
    {
        if (_vm is not null && _vm.IsPlaying && _vm.NowPlayingMode == NowPlayingMode.Vinyl
            && !MotionChrome.PreferReducedMotion)
        {
            _vinylAngle = (_vinylAngle + 1.2) % 360;
            _vinylRotation.Angle = _vinylAngle;
        }

        UpdateTimingLabels();
    }

    private void UpdateTimingLabels()
    {
        if (_vm is null) return;
        var pos = _vm.CurrentPositionSeconds;
        var dur = _vm.CurrentDurationSeconds;
        var rem = Math.Max(0, dur - pos);

        ElapsedLabel.Text = FormatTime(pos);
        RemainingLabel.Text = $"-{FormatTime(rem)}";
    }

    private static string FormatTime(double totalSeconds)
    {
        var s = (int)Math.Max(0, totalSeconds);
        var m = s / 60;
        var sec = s % 60;
        return $"{m}:{sec:D2}";
    }

    public void UpdateView()
    {
        if (_vm is null) return;

        // Mode switch Cover / Vinyl
        var isVinyl = _vm.NowPlayingMode == NowPlayingMode.Vinyl;
        CoverArtBorder.IsVisible = !isVinyl;
        VinylContainer.IsVisible = isVinyl;

        UpdateLayoutMode();
        UpdateLyrics();
        UpdateTimingLabels();
    }

    private void UpdateLayoutMode()
    {
        if (_vm is null) return;
        var width = Bounds.Width;
        var isSplit = width >= NowPlayingLayout.SplitBreakpoint;

        if (isSplit)
        {
            StageGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
            LyricsColumn.IsVisible = true;
            Grid.SetColumnSpan(StageGrid.Children[0], 1);
        }
        else
        {
            StageGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            var showFullscreenLyrics = _vm.NowPlayingLyricsMode == NowPlayingLyricsMode.Fullscreen;
            LyricsColumn.IsVisible = showFullscreenLyrics;
            StageGrid.Children[0].IsVisible = !showFullscreenLyrics;
        }
    }

    private void OnLyricsChanged()
    {
        Dispatcher.UIThread.Post(UpdateLyrics);
    }

    private void UpdateLyrics()
    {
        if (_vm?.Lyrics is null) return;
        var lyrics = _vm.Lyrics;

        if (_lyricsBlocks.Count != lyrics.Lines.Count)
        {
            LyricsPanel.Children.Clear();
            _lyricsBlocks.Clear();

            foreach (var line in lyrics.Lines)
            {
                var btn = new Button
                {
                    Classes = { "nowPlayingLine" },
                    Tag = line
                };

                var tb = new TextBlock
                {
                    Text = line.Text,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemeBrushes.WhiteOverlay59,
                    TextWrapping = TextWrapping.Wrap
                };
                _lyricsBlocks.Add(tb);
                btn.Content = tb;

                if (line.TimeMs.HasValue)
                {
                    var sec = line.TimeMs.Value / 1000.0;
                    btn.Click += (_, _) => _vm.SeekToSeconds(sec);
                }

                LyricsPanel.Children.Add(btn);
            }
        }

        var activeIdx = lyrics.CurrentLineIndex;
        for (var i = 0; i < _lyricsBlocks.Count; i++)
        {
            var tb = _lyricsBlocks[i];
            if (i == activeIdx)
            {
                tb.FontSize = 24;
                tb.FontWeight = FontWeight.Bold;
                tb.Foreground = Brushes.White;
            }
            else
            {
                tb.FontSize = 18;
                tb.FontWeight = FontWeight.SemiBold;
                tb.Foreground = ThemeBrushes.WhiteOverlay59;
            }
        }

        if (activeIdx >= 0 && activeIdx < LyricsPanel.Children.Count)
        {
            LyricsPanel.Children[activeIdx].BringIntoView();
        }
    }

    private void OnSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_vm is not null && sender is Slider slider)
        {
            _vm.SeekToSeconds(slider.Value);
        }
    }
}
