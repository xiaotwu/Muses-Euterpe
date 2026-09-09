using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class SettingsView : UserControl
{
    private ShellViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as ShellViewModel;
    }

    private void HideAllPages()
    {
        PageGeneral.IsVisible = false;
        PagePlayback.IsVisible = false;
        PageQuality.IsVisible = false;
        PageAppearance.IsVisible = false;
        PageYouTube.IsVisible = false;
        PageLyrics.IsVisible = false;
        PageDesktop.IsVisible = false;
        PageAbout.IsVisible = false;

        CatGeneralBtn.Classes.Remove("selected");
        CatPlaybackBtn.Classes.Remove("selected");
        CatQualityBtn.Classes.Remove("selected");
        CatAppearanceBtn.Classes.Remove("selected");
        CatYouTubeBtn.Classes.Remove("selected");
        CatLyricsBtn.Classes.Remove("selected");
        CatDesktopBtn.Classes.Remove("selected");
        CatAboutBtn.Classes.Remove("selected");
    }

    private void OnSelectGeneral(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageGeneral.IsVisible = true;
        CatGeneralBtn.Classes.Add("selected");
    }

    private void OnSelectPlayback(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PagePlayback.IsVisible = true;
        CatPlaybackBtn.Classes.Add("selected");
    }

    private void OnSelectQuality(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageQuality.IsVisible = true;
        CatQualityBtn.Classes.Add("selected");
    }

    private void OnSelectAppearance(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageAppearance.IsVisible = true;
        CatAppearanceBtn.Classes.Add("selected");
    }

    private void OnSelectYouTube(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageYouTube.IsVisible = true;
        CatYouTubeBtn.Classes.Add("selected");
    }

    private void OnSelectLyrics(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageLyrics.IsVisible = true;
        CatLyricsBtn.Classes.Add("selected");
    }

    private void OnSelectDesktop(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageDesktop.IsVisible = true;
        CatDesktopBtn.Classes.Add("selected");
    }

    private void OnSelectAbout(object? sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageAbout.IsVisible = true;
        CatAboutBtn.Classes.Add("selected");
    }

    private void OnOpenMiniPlayer(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            MiniPlayerWindow.ShowOrActivate(_vm);
        }
    }

    private void OnOpenDesktopLyrics(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            if (DesktopLyricsWindow.IsOpen)
            {
                DesktopLyricsWindow.CloseActive();
            }
            else
            {
                DesktopLyricsWindow.ShowOrActivate(_vm);
            }
        }
    }

    private void OnOpenGitHub(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/xiaotwu/Muses",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failure
        }
    }
}
