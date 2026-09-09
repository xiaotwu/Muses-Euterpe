using Muses.Core.Domain;

namespace Muses.Infrastructure.Playback;

public sealed class FakePlayerEngine : IPlayerEngine
{
    public PlayerState State { get; } = new();
    public Action? OnCompletion { get; set; }
    public int LoadCallCount { get; private set; }
    public TrackSnapshot? LastLoadedTrack { get; private set; }
    public int PlayCallCount { get; private set; }
    public int PauseCallCount { get; private set; }
    public int ToggleCallCount { get; private set; }
    public int SeekCallCount { get; private set; }
    public double? LastSeekTime { get; private set; }
    public float? VolumeSet { get; private set; }
    public int PrepareCallCount { get; private set; }
    public int PlayPreparedCallCount { get; private set; }
    public bool PlayPreparedReturnValue { get; set; }
    public bool SpectrumTapInstalled { get; private set; }
    public int SetEqCallCount { get; private set; }
    public IReadOnlyList<EqBand>? LastEqBands { get; private set; }
    public bool SupportsEq { get; set; } = true;

    public Task LoadAsync(TrackSnapshot track, CancellationToken cancellationToken = default)
    {
        LoadCallCount++;
        LastLoadedTrack = track;
        State.Track = track;
        State.Duration = track.DurationSeconds;
        State.Position = 0;
        State.IsPlaying = true;
        State.Error = null;
        return Task.CompletedTask;
    }

    public Task PrepareAsync(TrackSnapshot track, CancellationToken cancellationToken = default)
    {
        PrepareCallCount++;
        return Task.CompletedTask;
    }

    public bool PlayPrepared()
    {
        PlayPreparedCallCount++;
        return PlayPreparedReturnValue;
    }

    public void Play() { PlayCallCount++; State.IsPlaying = true; }
    public void Pause() { PauseCallCount++; State.IsPlaying = false; }
    public void Toggle() { ToggleCallCount++; State.IsPlaying = !State.IsPlaying; }
    public void Seek(double time) { SeekCallCount++; LastSeekTime = time; State.Position = time; }
    public void SetVolume(float value) => VolumeSet = value;

    public void SetEq(IReadOnlyList<EqBand> bands)
    {
        SetEqCallCount++;
        LastEqBands = bands.ToList();
    }

    public void InstallSpectrumTap(Action<SpectrumFrame> handler) => SpectrumTapInstalled = true;
    public void RemoveSpectrumTap() => SpectrumTapInstalled = false;

    public void Complete() => OnCompletion?.Invoke();
}
