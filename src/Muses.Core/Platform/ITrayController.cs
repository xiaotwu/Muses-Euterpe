namespace Muses.Core.Platform;

/// <summary>System tray / notification-area controller. Controlled by PrefKey.FfTray.</summary>
public interface ITrayController
{
    bool IsAvailable { get; }
    void SetEnabled(bool enabled);
    void Refresh();
}
