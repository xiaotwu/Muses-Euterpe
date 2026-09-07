using Avalonia.Input;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using Muses.App.ViewModels;
using Muses.Core.Chrome;
using Muses.Core.Preferences;
using Muses.Core.System;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => GlassChrome.Attach(this);
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = WindowChromeMetrics.CaptionDragHeight;
        TitleBar.ButtonBackgroundColor = Colors.Transparent;
        TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        TitleBar.ForegroundColor = ThemeBrushes.TextPrimaryColor;
        TitleBar.ButtonForegroundColor = ThemeBrushes.TextPrimaryColor;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.RequestOpenSearchWindow += (q, s) => SearchWindow.ShowOrActivate(this, vm, q, s);
            }
        };
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
            if (vm.SettingsOpen) { vm.SettingsOpen = false; e.Handled = true; return; }
            if (vm.PasteOpen) { vm.PasteOpen = false; e.Handled = true; return; }
            if (vm.IsLyricsDrawerOpen) { vm.IsLyricsDrawerOpen = false; e.Handled = true; return; }
            if (vm.IsQueueDrawerOpen) { vm.IsQueueDrawerOpen = false; e.Handled = true; return; }
            if (vm.IsNowPlayingOpen) { vm.IsNowPlayingOpen = false; e.Handled = true; return; }
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
                vm.SettingsOpen = true;
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
        e.DragEffects = e.Data.Contains(DataFormats.Text) || e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        var text = e.Data.GetText();
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
