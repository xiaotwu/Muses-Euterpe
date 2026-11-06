using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muses.App.ViewModels;
using Muses.Core.Preferences;
using Muses.Core.System;

namespace Muses.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
    }

    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel vm || e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Left or Key.Right))
            return;

        if (e.Key == Key.Left && vm.IsNowPlayingOpen)
            vm.ToggleNowPlaying();
        else
        {
            var command = e.Key == Key.Left ? vm.NavigateBackCommand : vm.NavigateForwardCommand;
            if (command.CanExecute(null)) command.Execute(null);
        }

        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;

        // Escape dismisses open overlays and drawers
        if (e.Key == Key.Escape)
        {
            if (vm.IsSaveEQPresetOpen) { vm.IsSaveEQPresetOpen = false; e.Handled = true; return; }
            if (vm.IsFocusOpen) { vm.IsFocusOpen = false; e.Handled = true; return; }
            if (vm.IsEQOpen) { vm.IsEQOpen = false; e.Handled = true; return; }
            if (vm.IsAudioNerdOpen) { vm.IsAudioNerdOpen = false; e.Handled = true; return; }
            if (vm.IsNotesOpen) { vm.IsNotesOpen = false; e.Handled = true; return; }
            if (vm.IsInbox) { vm.SelectedSection = SidebarSection.Home; e.Handled = true; return; }
            if (vm.SettingsOpen) { vm.CloseSettingsCommand.Execute(null); e.Handled = true; return; }
            if (vm.PasteOpen) { vm.PasteOpen = false; e.Handled = true; return; }
            if (vm.IsLyricsDrawerOpen) { vm.IsLyricsDrawerOpen = false; e.Handled = true; return; }
            if (vm.IsQueueDrawerOpen) { vm.IsQueueDrawerOpen = false; e.Handled = true; return; }
            if (vm.IsNowPlayingOpen) { vm.IsNowPlayingOpen = false; e.Handled = true; return; }
            if (vm.IsSearch) { vm.ReturnFromSearch(); e.Handled = true; return; }
        }

        // F11 toggles Now Playing fullscreen
        if (e.Key == Key.F11)
        {
            vm.ToggleNowPlaying();
            e.Handled = true;
            return;
        }

        // Space toggles playback when not focused in an input box
        if (e.Key == Key.Space && e.Source is not Avalonia.Controls.TextBox)
        {
            vm.Commands.Execute(CommandRegistry.TogglePlayback);
            e.Handled = true;
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!ctrl) return;

        // App-chrome shortcuts (search/settings/paste/drawers) remain available.
        // In-window Ctrl+P / Ctrl+Left / Ctrl+Right always work when focused. FfGlobalHotkeys is the optional system-wide RegisterHotKey hook (default off).
        switch (e.Key)
        {
            case Key.F:
                vm.OpenSearchWindow();
                e.Handled = true;
                break;
            case Key.P:
                vm.Commands.Execute(CommandRegistry.TogglePlayback);
                e.Handled = true;
                break;
            case Key.Left:
                vm.Commands.Execute(CommandRegistry.Previous);
                e.Handled = true;
                break;
            case Key.Right:
                vm.Commands.Execute(CommandRegistry.Next);
                e.Handled = true;
                break;
            case Key.OemComma:
                vm.OpenSettingsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V:
                vm.OpenPasteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.L:
                vm.ToggleLyricsDrawer();
                e.Handled = true;
                break;
            case Key.U:
                vm.ToggleQueueDrawer();
                e.Handled = true;
                break;
            case Key.E:
                vm.IsEQOpen = !vm.IsEQOpen;
                e.Handled = true;
                break;
            case Key.N:
                vm.IsNotesOpen = !vm.IsNotesOpen;
                e.Handled = true;
                break;
            case Key.I:
                vm.SelectedSection = vm.IsInbox ? SidebarSection.Home : SidebarSection.Inbox;
                e.Handled = true;
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.Text)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        var text = e.DataTransfer.TryGetText();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!text.Contains("youtu", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await vm.PlayUrlAsync(text);
            vm.RefreshPlayback();
        }
        catch (Exception ex)
        {
            vm.PasteError = ex.Message;
            vm.PasteOpen = true;
        }
    }
}
