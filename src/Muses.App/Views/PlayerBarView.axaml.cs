using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Muses.App.Controls.Playback;
using Muses.App.Theme;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class PlayerBarView : UserControl
{
    private static readonly TransformOperations PlayingScale = TransformOperations.Parse("scale(1.04)");
    internal Rect? GetArtworkFrame(Visual relativeTo) =>
        DockArtwork.IsEffectivelyVisible ? CoverArtworkMorph.GetFrame(DockArtwork, relativeTo) : null;

    internal void RestoreArtworkFocus() => ArtworkButton.Focus(NavigationMethod.Tab);

    private ShellViewModel? _vm;
    private IPointer? _scrubPointer;
    private Guid? _scrubTrack;

    public PlayerBarView()
    {
        InitializeComponent();
        DockArtwork.RenderTransform = TransformOperations.Identity;
        RefreshMotionPreference();
        DataContextChanged += (_, _) => ConnectViewModel();
        SizeChanged += (_, _) => Classes.Set("compact", Bounds.Width < 510);
        ProgressTrackContainer.SizeChanged += (_, _) => UpdateProgress();
    }

    internal void RefreshMotionPreference()
    {
        var animate = !MotionChrome.PreferReducedMotion;
        Classes.Set("motionEnabled", animate);
        DockArtwork.Transitions = animate
            ? [new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = MotionChrome.OverlayDuration,
                Easing = new SineEaseInOut(),
            }]
            : null;
        UpdateArtworkScale();
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

    private void ConnectViewModel()
    {
        DisconnectViewModel();
        if (!this.IsAttachedToVisualTree()) return;

        _vm = DataContext as ShellViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateArtworkScale();
        UpdateProgress();
    }

    private void DisconnectViewModel()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = null;
        _scrubPointer?.Capture(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ProgressFraction) or nameof(ShellViewModel.HasTrack))
            Dispatcher.UIThread.Post(UpdateProgress);
        else if (e.PropertyName == nameof(ShellViewModel.IsPlaying))
            Dispatcher.UIThread.Post(UpdateArtworkScale);
    }

    private void UpdateArtworkScale() => DockArtwork.RenderTransform =
        _vm?.IsPlaying == true && !MotionChrome.PreferReducedMotion ? PlayingScale : TransformOperations.Identity;

    private void UpdateProgress()
    {
        if (_scrubPointer is not null) return;

        SetProgress(_vm?.ProgressFraction ?? 0);
        Timing.Text = _vm is { HasTrack: true } vm
            ? $"{FormatTime(vm.CurrentPositionSeconds)} / −{FormatTime(vm.CurrentDurationSeconds - vm.CurrentPositionSeconds)}"
            : "— / —";
    }

    private void SetProgress(double fraction)
    {
        var width = ProgressTrackContainer.Bounds.Width;
        var fill = Math.Clamp(fraction * width, 0, width);
        ProgressTrackFill.Width = fill;
        ProgressThumb.Margin = new Thickness(Math.Max(0, fill - 4), -2.5, 0, 0);
    }

    private void SetScrubberEmphasis(bool emphasized)
    {
        ProgressTrackBg.Height = emphasized ? 4.5 : 3;
        ProgressTrackFill.Height = ProgressTrackBg.Height;
        ProgressThumb.IsVisible = emphasized;
    }

    private void OnProgressPointerEntered(object? sender, PointerEventArgs e) =>
        SetScrubberEmphasis(_vm is { CurrentDurationSeconds: > 0 });

    private void OnProgressPointerExited(object? sender, PointerEventArgs e) =>
        SetScrubberEmphasis(_scrubPointer is not null);

    private void OnProgressPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is not { CurrentDurationSeconds: > 0 } || _scrubPointer is not null
            || !e.GetCurrentPoint(ProgressTrackContainer).Properties.IsLeftButtonPressed)
            return;

        _scrubTrack = _vm.Playback?.State.Track?.Id;
        _scrubPointer = e.Pointer;
        e.Pointer.Capture(ProgressTrackContainer);
        SetScrubberEmphasis(true);
        SetProgress(FractionAt(e));
        e.Handled = true;
    }

    private void OnProgressPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer == _scrubPointer)
            SetProgress(FractionAt(e));
    }

    private void OnProgressPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _scrubPointer) return;

        if (_vm is { CurrentDurationSeconds: > 0 } vm && _scrubTrack == vm.Playback?.State.Track?.Id)
            vm.SeekToSeconds(FractionAt(e) * vm.CurrentDurationSeconds);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnProgressPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _scrubPointer = null;
        _scrubTrack = null;
        SetScrubberEmphasis(false);
        UpdateProgress();
    }

    private double FractionAt(PointerEventArgs e)
    {
        var width = ProgressTrackContainer.Bounds.Width;
        return width > 0 ? Math.Clamp(e.GetPosition(ProgressTrackContainer).X / width, 0, 1) : 0;
    }

    private static string FormatTime(double seconds)
    {
        var wholeSeconds = (int)Math.Max(0, seconds);
        return $"{wholeSeconds / 60}:{wholeSeconds % 60:D2}";
    }
}
