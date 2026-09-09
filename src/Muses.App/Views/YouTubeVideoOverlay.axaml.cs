using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muses.App.ViewModels;
using Muses.Core.L10n;
using Muses.Core.YouTube;

namespace Muses.App.Views;

public partial class YouTubeVideoOverlay : UserControl
{
    private ShellViewModel? _vm;

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
        if (e.Property == IsVisibleProperty && IsVisible)
        {
            Focus();
            SyncEmbedSurface();
        }
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
        var available = _vm?.IsVideoEmbedAvailable == true;
        DegradedPanel.IsVisible = !available;
        EmbedHost.IsVisible = available;
        // Live WebView host is not wired on this TFM; EmbedHost stays empty until Win11 WebView2 lands.
        // Policy still tears down the IYouTubeIFrameClient on close.
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
