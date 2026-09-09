namespace Muses.Core.Platform;

/// <summary>
/// Optional system-wide transport chords. Default off. In-window Ctrl+P / Left / Right
/// stay on MainWindow and are never gated by this.
/// </summary>
public interface IGlobalHotkeys : IDisposable
{
    bool IsSupported { get; }
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);

    event Action? PlayPausePressed;
    event Action? NextPressed;
    event Action? PreviousPressed;
}

public sealed class NullGlobalHotkeys : IGlobalHotkeys
{
    public static NullGlobalHotkeys Instance { get; } = new();
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
    public event Action? PlayPausePressed { add { } remove { } }
    public event Action? NextPressed { add { } remove { } }
    public event Action? PreviousPressed { add { } remove { } }
    public void Dispose() { }
}
