using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Muses.Core.Preferences;
using Muses.Platform.Windows;

namespace Muses.App.Services;

/// <summary>
/// Owns the Avalonia TrayIcon. When PrefKey.FfTray is off, or the platform helper is missing, stays idle.
/// Does not force tray creation on macOS when the native helper cannot provide an icon.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly WindowsTrayController _tray;
    private readonly IPreferences _preferences;
    private TrayIcon? _icon;
    private bool _disposed;

    public TrayIconHost(WindowsTrayController tray, IPreferences preferences)
    {
        _tray = tray;
        _preferences = preferences;
        _tray.Changed += OnTrayChanged;
    }

    public void Apply()
    {
        if (!_tray.IsEnabled)
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
            // macOS / headless: helper may be missing — do not force tray.
            DisposeIcon();
        }
    }

    private void OnTrayChanged() => Apply();

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
        if (_icon is null) return;
        _icon.IsVisible = false;
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
