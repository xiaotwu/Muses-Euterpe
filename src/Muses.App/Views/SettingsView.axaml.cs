using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Muses.App.ViewModels;
using Muses.Core.L10n;

namespace Muses.App.Views;

public partial class SettingsView : UserControl
{
    private ShellViewModel? _vm;
    private string? _webHomeConsentChannel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SetViewModel(DataContext as ShellViewModel);
        AttachedToVisualTree += (_, _) => SetViewModel(DataContext as ShellViewModel);
        DetachedFromVisualTree += (_, _) => SetViewModel(null);
    }

    private void SetViewModel(ShellViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null)
            _vm.PropertyChanged -= OnSettingsChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnSettingsChanged;
        CloseWebHomeConsent();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is { SettingsOpen: true, IsSettingsYouTube: true, IsAccountSignedIn: true, WebHomeEnabled: false } vm &&
            vm.Account?.Profile?.ChannelId == _webHomeConsentChannel)
            return;
        CloseWebHomeConsent();
    }

    private void OnReviewWebHomeConsent(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { CanSelectAccountHome: true, IsAccountSignedIn: true, WebHomeEnabled: false } vm ||
            vm.Account?.Profile?.ChannelId is not { Length: > 0 } channel) return;

        _webHomeConsentChannel = channel;
        WebHomeConsentPanel.IsVisible = true;
        ReviewWebHomeConsent.IsVisible = false;
        CancelWebHomeConsent.Focus();
    }

    private void OnCancelWebHomeConsent(object? sender, RoutedEventArgs e)
    {
        CloseWebHomeConsent();
        ReviewWebHomeConsent.Focus();
    }

    private void OnWebHomeConsentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        OnCancelWebHomeConsent(sender, e);
    }

    private void OnConfirmWebHomeConsent(object? sender, RoutedEventArgs e)
    {
        if (!WebHomeConsentPanel.IsVisible ||
            _vm is not { CanSelectAccountHome: true, IsAccountSignedIn: true, WebHomeEnabled: false } vm ||
            vm.Account?.Profile?.ChannelId != _webHomeConsentChannel)
            return;

        CloseWebHomeConsent();
        vm.EnableWebHomeCommand.Execute(null);
        CheckWebHomeSession.Focus();
    }

    private void CloseWebHomeConsent()
    {
        if (!WebHomeConsentPanel.IsVisible) return;
        _webHomeConsentChannel = null;
        WebHomeConsentPanel.IsVisible = false;
        ReviewWebHomeConsent.IsVisible = true;
    }

    private async void OnImportMacLibrary(object? sender, RoutedEventArgs e)
    {
        if (_vm is { } vm)
        {
            await vm.ImportMacLibraryAsync(SelectMacLibraryBackupAsync);
        }
    }

    private async Task<string?> SelectMacLibraryBackupAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            throw new NotSupportedException("File picker is unavailable.");
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.Tr("Choose a Muses for Mac library backup", "选择 Mac 版 Muses 曲库备份"),
            FileTypeFilter = [new FilePickerFileType(L10n.Tr("SQLite backup", "SQLite 备份")) { Patterns = ["*.sqlite"] }]
        });
        if (files.Count == 0) return null;

        using var file = files[0];
        return file.TryGetLocalPath() ?? throw new IOException("The selected file has no local path.");
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
                FileName = "https://github.com/xiaotwu/Muses-Euterpe",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failure
        }
    }
}
