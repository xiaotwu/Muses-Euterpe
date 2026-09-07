using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class MiniPlayerWindow : Window
{
    private static MiniPlayerWindow? _instance;
    private readonly ShellViewModel _vm;

    public static void ShowOrActivate(ShellViewModel vm)
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        var win = new MiniPlayerWindow(vm);
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    public static void CloseActive()
    {
        _instance?.Close();
    }

    public MiniPlayerWindow() : this(new ShellViewModel())
    {
    }

    public MiniPlayerWindow(ShellViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnExpandClicked(object? sender, RoutedEventArgs e)
    {
        Close();
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Activate();
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
