using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Muses.App.Services;
using Muses.App.Theme;
using Muses.App.ViewModels;
using Muses.Core.NowPlaying;

namespace Muses.App.Views;

public partial class RootView : UserControl
{
    private const double MinimumMorphWidth = 960;
    private static readonly TimeSpan DismissDuration = TimeSpan.FromMilliseconds(300);
    private ShellViewModel? _shell;
    private Window? _window;
    private CancellationTokenSource? _presentationFade;
    private Guid? _transferTrack;

    public RootView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ConnectShell();
        SizeChanged += (_, _) => EndArtworkTransfer();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.Activated += OnWindowActivated;
        ConnectShell();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
            _window.Activated -= OnWindowActivated;
        _window = null;
        DisconnectShell();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        GlassChrome.Refresh();
        if (Application.Current is not { } app || !MotionChrome.Refresh(app))
            return;

        CancelPresentationFade();
        EndArtworkTransfer();
        var open = _shell?.IsNowPlayingOpen == true;
        NowPlaying.Opacity = open ? 1 : 0;
        NowPlaying.IsVisible = open;
        BrowseLayer.IsVisible = !open;
        PlayerDock.RefreshMotionPreference();
        NowPlaying.RefreshMotionPreference();
    }

    private void ConnectShell()
    {
        if (!this.IsAttachedToVisualTree() || ReferenceEquals(_shell, DataContext)) return;

        DisconnectShell();
        _shell = DataContext as ShellViewModel;
        if (_shell is not null)
            _shell.PropertyChanged += OnShellPropertyChanged;
        UpdateNowPlayingPresentation(animate: false);
    }

    private void DisconnectShell()
    {
        if (_shell is not null)
            _shell.PropertyChanged -= OnShellPropertyChanged;
        _shell = null;
        CancelPresentationFade();
        EndArtworkTransfer();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.IsNowPlayingOpen):
                UpdateNowPlayingPresentation(animate: true);
                break;
            case nameof(ShellViewModel.SettingsOpen):
            case nameof(ShellViewModel.IsVideoOverlayOpen):
                PlayerDock.IsVisible = _shell?.IsCapsuleVisible == true;
                break;
            case nameof(ShellViewModel.MacLibraryImportRevision):
                if (SongsPage.IsVisible) SongsPage.RefreshSongs();
                if (NewPage.IsVisible) NewPage.UpdateNewView();
                break;
        }

        if (_transferTrack is { } track && (_shell?.Playback?.State.Track?.Id != track
            || _shell.NowPlayingLyricsMode == NowPlayingLyricsMode.Fullscreen || _shell.IsVideoOverlayOpen
            || e.PropertyName == nameof(ShellViewModel.NowPlayingMode)))
            EndArtworkTransfer();
    }

    private void UpdateNowPlayingPresentation(bool animate)
    {
        var open = _shell?.IsNowPlayingOpen == true;
        animate &= !MotionChrome.PreferReducedMotion;
        var wasVisible = NowPlaying.IsVisible;
        var opacity = wasVisible ? NowPlaying.Opacity : 0;
        CancelPresentationFade();
        EndArtworkTransfer();

        if (open && animate && Bounds.Width >= MinimumMorphWidth
            && _shell is { NowPlayingLyricsMode: NowPlayingLyricsMode.Inline } vm
            && vm.Playback?.State.Track?.Id is { } track && PlayerDock.GetArtworkFrame(this) is { } origin)
        {
            _transferTrack = track;
            NowPlaying.SetCoverTransfer(true);
            CoverMorph.Prepare(origin, ArtworkLoader.Instance.GetCached(vm.NowPlayingArtworkUrl));
            LayoutUpdated += OnCoverLayoutUpdated;
        }

        PlayerDock.IsVisible = _shell?.IsCapsuleVisible == true;
        BrowseLayer.IsVisible = !open || (animate && opacity < 1);
        BrowseLayer.IsHitTestVisible = !open;
        KeyboardNavigation.SetTabNavigation(BrowseLayer, open ? KeyboardNavigationMode.None : KeyboardNavigationMode.Continue);
        NowPlaying.IsEnabled = open;
        NowPlaying.IsHitTestVisible = open;
        NowPlaying.SetPresentationActive(open);

        if (open)
        {
            NowPlaying.IsVisible = true;
            NowPlaying.Focus();
            NowPlaying.Opacity = 1;
            if (animate && opacity < 1)
                _ = FadeNowPlayingAsync(opacity, 1, MotionChrome.MorphDuration, new SineEaseInOut());
        }
        else if (wasVisible)
        {
            if (animate && opacity > 0)
                _ = FadeNowPlayingAsync(opacity, 0, DismissDuration, new SineEaseOut());
            else
                NowPlaying.IsVisible = false;

            Dispatcher.UIThread.Post(() =>
            {
                if (_shell?.IsCapsuleVisible == true && PlayerDock.IsEffectivelyVisible)
                    PlayerDock.RestoreArtworkFocus();
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnCoverLayoutUpdated(object? sender, EventArgs e)
    {
        if (!NowPlaying.IsArtworkArranged) return;
        LayoutUpdated -= OnCoverLayoutUpdated;
        if (NowPlaying.GetArtworkTarget(this) is { } destination)
            CoverMorph.FlyTo(destination.Frame, destination.CornerRadius, EndArtworkTransfer);
        else
            EndArtworkTransfer();
    }

    private void EndArtworkTransfer()
    {
        if (_transferTrack is null) return;
        _transferTrack = null;
        LayoutUpdated -= OnCoverLayoutUpdated;
        CoverMorph.Clear();
        NowPlaying.SetCoverTransfer(false);
    }

    private async Task FadeNowPlayingAsync(double from, double to, TimeSpan duration, Easing easing)
    {
        using var fade = new CancellationTokenSource();
        _presentationFade = fade;
        NowPlaying.Opacity = to;
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } },
            },
        };

        try
        {
            await animation.RunAsync(NowPlaying, fade.Token);
            if (!fade.IsCancellationRequested)
            {
                if (to == 0)
                    NowPlaying.IsVisible = false;
                else if (_shell?.IsNowPlayingOpen == true)
                    BrowseLayer.IsVisible = false;
            }
        }
        catch (OperationCanceledException) when (fade.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_presentationFade, fade)) _presentationFade = null;
        }
    }

    private void CancelPresentationFade()
    {
        var fade = _presentationFade;
        _presentationFade = null;
        fade?.Cancel();
    }

    private void OnPasteScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.PasteOpen = false;
    }

    private void OnAddChoiceScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.CloseAddChoice();
    }

    private void OnNewPlaylistScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.CloseNewPlaylistDialog();
    }

    private void OnImportScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.CloseImportDialog();
    }

    private void OnEQScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.IsEQOpen = false;
    }

    private void OnAudioNerdScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.IsAudioNerdOpen = false;
    }

    private void OnFocusScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.IsFocusOpen = false;
    }

    private void OnNotesScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.CloseNotes();
    }

    private void OnSaveEQPresetScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.IsSaveEQPresetOpen = false;
    }
}
