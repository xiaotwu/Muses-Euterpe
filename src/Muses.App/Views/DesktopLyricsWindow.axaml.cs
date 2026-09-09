using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class DesktopLyricsWindow : Window
{
    private static DesktopLyricsWindow? _instance;
    private readonly ShellViewModel _vm;

    public static void ShowOrActivate(ShellViewModel vm)
    {
        if (!Muses.Core.Preferences.FeatureFlagDefaults.IsEnabled(vm.Preferences, Muses.Core.Preferences.PrefKey.FfDesktopLyrics))
            return;
        ShowOrActivateCore(vm);
    }

    private static void ShowOrActivateCore(ShellViewModel vm)
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        var win = new DesktopLyricsWindow(vm);
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    public static void CloseActive()
    {
        _instance?.Close();
    }

    public static bool IsOpen => _instance is not null;

    public DesktopLyricsWindow() : this(new ShellViewModel())
    {
    }

    public DesktopLyricsWindow(ShellViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        if (_vm.Lyrics is not null)
        {
            _vm.Lyrics.Changed += OnLyricsChanged;
            UpdateLyrics();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm.Lyrics is not null)
        {
            _vm.Lyrics.Changed -= OnLyricsChanged;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnLyricsChanged()
    {
        Dispatcher.UIThread.Post(UpdateLyrics);
    }

    private void UpdateLyrics()
    {
        if (_vm.Lyrics is null || _vm.Lyrics.Lines.Count == 0)
        {
            LyricLineText.Text = string.IsNullOrEmpty(_vm.NowPlayingTitle)
                ? "♪ Muses Desktop Lyrics"
                : $"♪ {_vm.NowPlayingTitle} - {_vm.NowPlayingArtist}";
            TranslationText.IsVisible = false;
            return;
        }

        var idx = _vm.Lyrics.CurrentLineIndex;
        if (idx >= 0 && idx < _vm.Lyrics.Lines.Count)
        {
            var line = _vm.Lyrics.Lines[idx];
            LyricLineText.Text = line.Text;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                TranslationText.Text = line.Translation;
                TranslationText.IsVisible = true;
            }
            else
            {
                TranslationText.IsVisible = false;
            }
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
