using Muses.Core.Domain;
using Muses.Core.Platform;
using Muses.Core.Playback;
using Muses.Core.Preferences;
using Muses.Core.L10n;

namespace Muses.Platform.Windows;

/// <summary>
/// Tray contract used by the app shell. Avalonia TrayIcon is owned by Muses.App;
/// this type tracks enablement, Now Playing title, and routes menu actions to PlaybackService.
/// On macOS, IsAvailable stays true for the Avalonia helper path but PrefKey.FfTray still gates creation.
/// </summary>
public sealed class WindowsTrayController : ITrayController
{
    private readonly PlaybackService _playback;
    private readonly IPreferences _preferences;
    private readonly Action? _showMain;
    private readonly Action? _exit;
    private bool _enabled;

    public bool IsAvailable => true;
    public bool IsEnabled => _enabled;
    public string NowPlayingTitle { get; private set; } = "Muses";

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? Changed;

    public WindowsTrayController(
        PlaybackService playback,
        IPreferences preferences,
        Action? showMain = null,
        Action? exit = null)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _showMain = showMain;
        _exit = exit;
        _playback.EventBus.EventPosted += _ => Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (_enabled) Refresh();
        Changed?.Invoke();
    }

    public void Refresh()
    {
        var track = _playback.State.Track;
        NowPlayingTitle = track is null
            ? "Muses"
            : $"{track.Title} — {track.Artist}";
        Changed?.Invoke();
    }

    public void RequestPlayPause()
    {
        PlayPauseRequested?.Invoke();
        _playback.Toggle();
    }

    public void RequestNext()
    {
        NextRequested?.Invoke();
        _playback.Next();
    }

    public void RequestPrevious()
    {
        PreviousRequested?.Invoke();
        _playback.Previous();
    }

    public void RequestShow()
    {
        ShowRequested?.Invoke();
        _showMain?.Invoke();
    }

    public void RequestExit()
    {
        ExitRequested?.Invoke();
        _exit?.Invoke();
    }

    public bool ShouldEnableFromPrefs() => FeatureFlagDefaults.IsEnabled(_preferences, PrefKey.FfTray);

    public bool CloseToTray => _preferences.GetBool(PrefKey.CloseToTray, true);

    public string PlayLabel => L10n.Tr("Play", "播放");
    public string PauseLabel => L10n.Tr("Pause", "暂停");
    public string NextLabel => L10n.Tr("Next", "下一首");
    public string PreviousLabel => L10n.Tr("Previous", "上一首");
    public string ShowLabel => L10n.Tr("Show Muses", "显示 Muses");
    public string ExitLabel => L10n.Tr("Exit", "退出");
}
