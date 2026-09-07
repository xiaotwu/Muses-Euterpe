namespace Muses.Core.Domain;

public sealed class PlayerState
{
    public TrackSnapshot? Track { get; set; }
    public bool IsPlaying { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
    public bool Buffering { get; set; }
    public double BufferRatio { get; set; }
    public PlayerError? Error { get; set; }
}

public enum PlayerErrorKind
{
    SourceUnavailable,
    NetworkError,
    FileMissing,
    DecodingFailed,
    EngineStartFailed,
    RateLimited
}

public sealed record PlayerError(PlayerErrorKind Kind, string? Detail = null);

public readonly record struct EqBand(double Frequency, float Gain, float Q);

public readonly record struct SpectrumFrame(float[] Magnitudes);

public interface IPlayerEngine
{
    PlayerState State { get; }
    Action? OnCompletion { get; set; }
    Task LoadAsync(TrackSnapshot track, CancellationToken cancellationToken = default);
    Task PrepareAsync(TrackSnapshot track, CancellationToken cancellationToken = default);
    bool PlayPrepared();
    void Play();
    void Pause();
    void Toggle();
    void Seek(double time);
    void SetVolume(float value);
    void SetEq(IReadOnlyList<EqBand> bands);
    void InstallSpectrumTap(Action<SpectrumFrame> handler);
    void RemoveSpectrumTap();
}
