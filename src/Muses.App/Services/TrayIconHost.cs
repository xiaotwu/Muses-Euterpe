using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Muses.Platform.Windows;

namespace Muses.App.Services;

/// <summary>
/// Owns the Avalonia tray icon and tracks whether it can restore a hidden window.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly WindowsTrayController _tray;
    private readonly TrayIcons _icons;
    private TrayIcon? _icon;
    private bool _disposed;

    public bool CanRestoreWindow { get; private set; }
    public event Action? AvailabilityChanged;

    public TrayIconHost(Application application, WindowsTrayController tray)
    {
        _tray = tray;
        _icons = TrayIcon.GetIcons(application) ?? new TrayIcons();
        TrayIcon.SetIcons(application, _icons);
        _tray.Changed += OnTrayChanged;
    }

    public void Apply()
    {
        if (_disposed || !_tray.IsEnabled)
        {
            DisposeIcon();
            return;
        }

        try
        {
            if (_icon is null)
            {
                _icon = new TrayIcon
                {
                    IsVisible = true,
                    ToolTipText = _tray.NowPlayingTitle,
                    Menu = BuildMenu()
                };
                TrySetIcon(_icon);
                _icon.Clicked += (_, _) => Dispatcher.UIThread.Post(_tray.RequestShow);
                _icons.Add(_icon);
                SetAvailability(true);
            }
            else
            {
                _icon.ToolTipText = _tray.NowPlayingTitle;
                _icon.Menu = BuildMenu();
                _icon.IsVisible = true;
            }
        }
        catch
        {
            DisposeIcon();
        }
    }

    private void OnTrayChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void SetAvailability(bool available)
    {
        if (CanRestoreWindow == available)
            return;

        CanRestoreWindow = available;
        AvailabilityChanged?.Invoke();
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        var playPause = new NativeMenuItem(_tray.PlayLabel) { };
        // Title reflects current state loosely; Toggle handles play/pause.
        playPause.Click += (_, _) => _tray.RequestPlayPause();
        menu.Add(playPause);

        var next = new NativeMenuItem(_tray.NextLabel);
        next.Click += (_, _) => _tray.RequestNext();
        menu.Add(next);

        var prev = new NativeMenuItem(_tray.PreviousLabel);
        prev.Click += (_, _) => _tray.RequestPrevious();
        menu.Add(prev);

        menu.Add(new NativeMenuItemSeparator());

        var show = new NativeMenuItem(_tray.ShowLabel);
        show.Click += (_, _) => _tray.RequestShow();
        menu.Add(show);

        var exit = new NativeMenuItem(_tray.ExitLabel);
        exit.Click += (_, _) => _tray.RequestExit();
        menu.Add(exit);
        return menu;
    }

    private static void TrySetIcon(TrayIcon icon)
    {
        try
        {
            var uri = new Uri("avares://Muses/Assets/icon.ico");
            if (AssetLoader.Exists(uri))
                icon.Icon = new WindowIcon(AssetLoader.Open(uri));
        }
        catch
        {
            // Icon optional
        }
    }

    private void DisposeIcon()
    {
        SetAvailability(false);
        if (_icon is null) return;
        _icon.IsVisible = false;
        _icons.Remove(_icon);
        _icon.Dispose();
        _icon = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.Changed -= OnTrayChanged;
        DisposeIcon();
    }
}
