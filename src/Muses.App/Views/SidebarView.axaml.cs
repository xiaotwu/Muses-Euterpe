using Avalonia.Controls;
using Avalonia.Interactivity;
using Muses.App.ViewModels;
using Muses.Core.Domain;

namespace Muses.App.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    private void OnPinnedPlaylistClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PlaylistEntity playlist } && DataContext is ShellViewModel vm)
        {
            vm.SelectPlaylist(playlist);
        }
    }
}
