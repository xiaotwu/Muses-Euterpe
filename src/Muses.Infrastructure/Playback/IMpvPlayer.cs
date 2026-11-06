using Muses.Infrastructure.YTDlp;

namespace Muses.Infrastructure.Playback;

/// <summary>
/// One live mpv process + JSON IPC session. Tests inject a fake; production uses
/// <see cref="MpvPlayerFactory"/>.
/// </summary>
public interface IMpvPlayerSession : IDisposable
{
    /// <summary>True while the host process has not exited.</summary>
    bool IsAlive { get; }

    /// <summary>Last known media time in seconds (may be updated by the session itself).</summary>
    double TimePos { get; }
    double? Duration => null;

    void SetPause(bool paused);
    void SeekAbsolute(double seconds);
    /// <summary>mpv volume property, 0..100.</summary>
    void SetVolume(int volume0to100);

    /// <summary>Replace the mpv audio filter chain (e.g. equalizer bands). Empty clears.</summary>
    void SetAudioFilters(string? afChain);

    /// <summary>Query time-pos from the player. Returns null if unavailable.</summary>
    Task<double?> GetTimePosAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised once when mpv reports end-file with reason eof.</summary>
    event Action? EndOfFile;

    /// <summary>Raised when the host process exits for any reason.</summary>
    event Action? Exited;
}

/// <summary>
/// Locates mpv and starts a session for a resolved stream URL.
/// </summary>
public interface IMpvPlayerFactory
{
    /// <summary>Absolute path to mpv, or null if not found on PATH / resources.</summary>
    string? FindBinary();

    /// <summary>
    /// Open the resolved stream and wait for mpv to load it in a paused session.
    /// </summary>
    Task<IMpvPlayerSession> StartAsync(
        ResolvedAudioStream stream, int initialVolume0to100, CancellationToken cancellationToken);
}
