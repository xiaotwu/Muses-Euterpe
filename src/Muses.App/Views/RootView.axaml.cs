using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muses.App.Theme;
using Muses.App.ViewModels;
using Muses.Core.Chrome;

namespace Muses.App.Views;

public partial class RootView : UserControl
{
    public RootView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => SyncGlassLayers();
    }

    private void SyncGlassLayers()
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            GlassChrome.Attach(top);

        GlassChrome.SyncAcrylic(this.FindControl<ExperimentalAcrylicBorder>("SidebarAcrylic"), MusesGlassRole.PersistentChrome);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.SettingsOpen = false;
    }

    private void OnCloseSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.SettingsOpen = false;
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
