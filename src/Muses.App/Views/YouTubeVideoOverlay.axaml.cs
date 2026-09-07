using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class YouTubeVideoOverlay : UserControl
{
    private ShellViewModel? _vm;

    public YouTubeVideoOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as ShellViewModel;
        KeyDown += OnUserControlKeyDown;
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
        var url = $"https://www.youtube.com/watch?v={_vm.NowPlayingYouTubeId}";
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
