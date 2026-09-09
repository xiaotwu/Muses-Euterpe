using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muses.App.Services;
using Muses.App.ViewModels;
using Muses.Core.L10n;
using Muses.Core.YouTube;

namespace Muses.App.Views;

public partial class YouTubeVideoOverlay : UserControl
{
    private ShellViewModel? _vm;
    private Control? _attachedHost;

    public YouTubeVideoOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as ShellViewModel;
            ApplyLocalizedChrome();
            SyncEmbedSurface();
        };
        KeyDown += OnUserControlKeyDown;
        PropertyChanged += OnControlPropertyChanged;
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
            SyncEmbedSurface();
        if (e.Property == IsVisibleProperty && IsVisible)
            Focus();
    }

    private void ApplyLocalizedChrome()
    {
        TitleLabel.Text = L10n.Tr("Video", "视频");
        SuspensionNote.Text = L10n.Tr(
            "Native audio playback is suspended while the video stage is active.",
            "视频舞台打开时，原生音频已暂停。");
        OpenInYouTubeLabel.Text = L10n.Tr("Open in YouTube", "在 YouTube 中打开");
    }

    private void SyncEmbedSurface()
    {
        var available = _vm?.IsVideoEmbedAvailable == true && IsVisible;
        DegradedPanel.IsVisible = !available;
        EmbedHost.IsVisible = available;

        if (!available)
        {
            DetachHost();
            return;
        }

        if (_vm?.VideoIFrameClient is WindowsWebView2YouTubeIFrameClient win)
        {
            if (!ReferenceEquals(_attachedHost, win.HostControl))
            {
                DetachHost();
                EmbedHost.Children.Clear();
                EmbedHost.Children.Add(win.HostControl);
                _attachedHost = win.HostControl;
            }
        }
    }

    private void DetachHost()
    {
        if (_attachedHost is null) return;
        EmbedHost.Children.Remove(_attachedHost);
        _attachedHost = null;
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm?.CloseVideoOverlay();
        e.Handled = true;
    }

    private void OnUserControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm?.CloseVideoOverlay();
            e.Handled = true;
        }
    }

    private void OnOpenInBrowserClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || string.IsNullOrEmpty(_vm.NowPlayingYouTubeId)) return;
        var url = YouTubeEmbed.WatchUrl(_vm.NowPlayingYouTubeId);
        if (url is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failure to open browser
        }
    }
}
