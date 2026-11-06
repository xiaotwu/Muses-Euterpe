using Muses.Core.Domain;
using Muses.Infrastructure.YTDlp;

namespace Muses.Infrastructure.Playback;

/// <summary>
/// Production engine: yt-dlp resolves a stream URL, then mpv emits audio and is
/// controlled over JSON IPC (<c>--input-ipc-server</c>). Pause/seek/volume do not
/// kill the process; completion comes from mpv end-file eof (not wall-clock).
/// </summary>
public sealed class ProcessStreamEngine : IPlayerEngine, IDisposable
{
    private readonly IYTDlpBridge _bridge;
    private readonly IMpvPlayerFactory _mpv;
    private readonly SynchronizationContext? _ownerContext = SynchronizationContext.Current;
    private string _quality;
    private bool _replayGainEnabled = true;
    private double? _replayGainDb;
    private readonly object _gate = new();

    private CancellationTokenSource? _loadCts;
    private IMpvPlayerSession? _session;
    private Timer? _positionTimer;
    private int _seekGeneration;
    private float _volume = 0.8f;
    private IReadOnlyList<EqBand> _eqBands = Array.Empty<EqBand>();
    private double _crossfadeSeconds;
    private IMpvPlayerSession? _outgoing;
    private int _completionRaised;
    private int _disposed;

    public ProcessStreamEngine(IYTDlpBridge bridge, string quality = "bestaudio")
        : this(bridge, new MpvPlayerFactory(), quality)
    {
    }

    public ProcessStreamEngine(IYTDlpBridge bridge, IMpvPlayerFactory mpvFactory, string quality = "bestaudio")
    {
        _bridge = bridge;
        _mpv = mpvFactory;
        _quality = quality;
    }

    public PlayerState State { get; } = new();
    public Action? OnCompletion { get; set; }

    /// <summary>Map Settings PrefKey.AudioQuality values onto yt-dlp format keys (audio-only).</summary>
    public static string MapAudioQualityPref(string? pref) => pref switch
    {
        "high" => "128k",
        "medium" => "64k",
        "256k" => "256k",
        "128k" => "128k",
        "64k" => "64k",
        "bestaudio" => "bestaudio",
        // "best" in Settings means best audio, never the video+audio mux format.
        _ => "bestaudio"
    };

    public void SetStreamQuality(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality)) return;
        TrackSnapshot? reload = null;
        lock (_gate)
        {
            if (string.Equals(_quality, quality, StringComparison.Ordinal)) return;
            _quality = quality;
            if (State.Track is not null && (State.IsPlaying || State.Buffering || _session is not null))
                reload = State.Track;
        }

        if (reload is not null)
            _ = ReloadForQualityAsync(reload);
    }

    private async Task ReloadForQualityAsync(TrackSnapshot track)
    {
        var position = State.Position;
        var paused = !State.IsPlaying;
        try
        {
            await LoadCoreAsync(track, paused, CancellationToken.None, position).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch
        {
            // LoadAsync records State.Error.
        }
    }

    public void SetReplayGainEnabled(bool enabled)
    {
        lock (_gate)
        {
            _replayGainEnabled = enabled;
            ApplyReplayGain_NoLock();
        }
    }

    public Task LoadAsync(TrackSnapshot track, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(track, startPaused: false, cancellationToken);

    public Task LoadPausedAsync(TrackSnapshot track, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(track, startPaused: true, cancellationToken);

    private async Task LoadCoreAsync(
        TrackSnapshot track, bool startPaused, CancellationToken cancellationToken, double? startPosition = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        CancellationTokenSource loadCts;
        CancellationToken ct;
        string quality;
        lock (_gate)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ct = loadCts.Token;
            _loadCts = loadCts;
            if (_crossfadeSeconds >= 0.05 && State.IsPlaying && _session is { IsAlive: true })
                DetachOutgoing_NoLock();
            else
                StopSession_NoLock();

            _completionRaised = 0;
            State.Track = track;
            _replayGainDb = track.ReplayGain;
            State.Duration = track.DurationSeconds;
            State.Position = 0;
            State.Buffering = true;
            State.Error = null;
            State.IsPlaying = false;
            quality = _quality;
        }

        var errorKind = PlayerErrorKind.EngineStartFailed;
        ResolvedAudioStream? stream = null;
        IMpvPlayerSession? session = null;
        try
        {
            if (_mpv.FindBinary() is null)
                throw new InvalidOperationException("mpv binary not found on PATH or under resources/.");

            errorKind = PlayerErrorKind.NetworkError;
            stream = await _bridge.ResolveAudioStreamAsync(track.YouTubeId, quality, TimeSpan.FromSeconds(25), ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            errorKind = PlayerErrorKind.EngineStartFailed;
            session = await _mpv.StartAsync(stream, VolumeToMpv(_volume), ct).ConfigureAwait(false);
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                if (!session.IsAlive)
                    throw new MpvSourceException("mpv stopped while loading the audio stream.");

                AttachSession_NoLock(session, track, quality, stream);
                if (session.Duration is { } duration && double.IsFinite(duration) && duration > 0)
                    State.Duration = duration;
                session = null; // Ownership transferred to the engine.
                State.Buffering = false;
                if (startPosition is { } position) Seek(position);
                if (!startPaused) Play();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loadCts, loadCts))
                {
                    State.Buffering = false;
                    StopOutgoing_NoLock();
                }
            }
            throw;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loadCts, loadCts))
                {
                    StopSession_NoLock();
                    State.Buffering = false;
                    State.IsPlaying = false;
                    if (ex is MpvSourceException && stream is not null)
                    {
                        _bridge.InvalidateAudioStream(track.YouTubeId, quality, stream);
                        errorKind = PlayerErrorKind.SourceUnavailable;
                    }
                    State.Error = new PlayerError(errorKind, ex.Message);
                }
            }
            throw;
        }
        finally
        {
            session?.Dispose();
        }
    }

    public Task PrepareAsync(TrackSnapshot track, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool PlayPrepared() => false;

    public void Play()
    {
        lock (_gate)
        {
            if (State.Track is null) return;
            if (_session is null || !_session.IsAlive || _completionRaised != 0) return;
            if (State.IsPlaying) return;
            _session.SetPause(false);
            State.IsPlaying = true;
            StartPositionTimer_NoLock();
            if (_outgoing is not null) BeginCrossfade_NoLock();
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            State.IsPlaying = false;
            StopPositionTimer_NoLock();
            _session?.SetPause(true);
            if (_outgoing is not null)
            {
                StopOutgoing_NoLock();
                _session?.SetVolume(VolumeToMpv(_volume));
            }
            // Intentionally keep the session alive so Resume does not re-resolve yt-dlp.
        }
    }

    public void Toggle()
    {
        if (State.IsPlaying) Pause();
        else Play();
    }

    public void Seek(double time)
    {
        if (!double.IsFinite(time)) return;
        lock (_gate)
        {
            var clamped = Math.Clamp(time, 0, State.Duration > 0 ? State.Duration : double.MaxValue);
            _seekGeneration++;
            State.Position = clamped;
            _session?.SeekAbsolute(clamped);
        }
    }

    public void SetVolume(float value)
    {
        lock (_gate)
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _session?.SetVolume(VolumeToMpv(_volume));
        }
    }

    public bool SupportsEq => true;

    public void SetEq(IReadOnlyList<EqBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        lock (_gate)
        {
            _eqBands = bands.ToList();
            ApplyEq_NoLock();
        }
    }

    public void SetCrossfadeSeconds(double seconds)
    {
        lock (_gate)
        {
            _crossfadeSeconds = Math.Clamp(seconds, 0, 12);
        }
    }

    public void InstallSpectrumTap(Action<SpectrumFrame> handler) { }
    public void RemoveSpectrumTap() { }

    /// <summary>Build an mpv <c>af</c> chain from peaking equalizer bands (ISO freqs).</summary>
    public static string BuildEqAfChain(IReadOnlyList<EqBand> bands)
    {
        if (bands.Count == 0) return "";
        // Skip near-flat bands to keep the chain short; mpv equalizer uses octave width.
        var parts = new List<string>();
        foreach (var b in bands)
        {
            if (Math.Abs(b.Gain) < 0.05f) continue;
            var f = b.Frequency.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var g = b.Gain.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var q = (b.Q <= 0 ? 1f : b.Q).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add($"equalizer=f={f}:t=o:w={q}:g={g}");
        }
        return string.Join(",", parts);
    }

    private void ApplyEq_NoLock()
    {
        // EQ and ReplayGain share the af chain.
        ApplyAudioFilters_NoLock();
    }

    private void ApplyReplayGain_NoLock()
    {
        ApplyAudioFilters_NoLock();
    }

    private void ApplyAudioFilters_NoLock()
    {
        var parts = new List<string>();
        var eq = BuildEqAfChain(_eqBands);
        if (!string.IsNullOrWhiteSpace(eq))
            parts.Add(eq);

        if (_replayGainEnabled && _replayGainDb is { } rg && Math.Abs(rg) >= 0.05)
        {
            var g = rg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add("volume=" + g + "dB");
        }

        _session?.SetAudioFilters(parts.Count == 0 ? "" : string.Join(",", parts));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            StopPositionTimer_NoLock();
            StopSession_NoLock();
        }
    }

    private void DetachOutgoing_NoLock()
    {
        StopPositionTimer_NoLock();
        StopOutgoing_NoLock();
        if (_session is null) return;
        _outgoing = _session;
        _session = null;
    }

    private void BeginCrossfade_NoLock()
    {
        var outgoing = _outgoing;
        var incoming = _session;
        var seconds = _crossfadeSeconds;
        var target = VolumeToMpv(_volume);
        if (outgoing is null || incoming is null || seconds < 0.05) return;
        _ = Task.Run(async () =>
        {
            const int steps = 8;
            var slice = TimeSpan.FromSeconds(Math.Max(0.05, seconds / steps));
            for (var i = 1; i <= steps; i++)
            {
                await Task.Delay(slice).ConfigureAwait(false);
                var t = i / (double)steps;
                lock (_gate)
                {
                    if (!ReferenceEquals(_outgoing, outgoing) || !ReferenceEquals(_session, incoming)) return;
                    outgoing.SetVolume((int)Math.Round(target * (1 - t)));
                    incoming.SetVolume((int)Math.Round(target * t));
                }
            }
            lock (_gate)
            {
                if (ReferenceEquals(_outgoing, outgoing))
                    StopOutgoing_NoLock();
            }
        });
    }

    private void AttachSession_NoLock(
        IMpvPlayerSession session, TrackSnapshot track, string quality, ResolvedAudioStream stream)
    {
        _session = session;
        session.EndOfFile += () => HandleEndOfFile(session);
        session.Exited += () => HandleExited(session, track, quality, stream);
        session.SetVolume(_outgoing is null ? VolumeToMpv(_volume) : 0);
        ApplyEq_NoLock();
    }

    private void StopSession_NoLock()
    {
        StopPositionTimer_NoLock();
        var session = _session;
        _session = null;
        StopOutgoing_NoLock();
        session?.Dispose();
    }

    private void StopOutgoing_NoLock()
    {
        var outgoing = _outgoing;
        _outgoing = null;
        outgoing?.Dispose();
    }

    private void HandleEndOfFile(IMpvPlayerSession session)
    {
        if (_ownerContext is { } owner && !ReferenceEquals(SynchronizationContext.Current, owner))
            owner.Post(_ => CompleteCurrentSession(session), null);
        else
            CompleteCurrentSession(session);
    }

    private void CompleteCurrentSession(IMpvPlayerSession session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session) || _completionRaised != 0) return;
            _completionRaised = 1;
            State.IsPlaying = false;
            if (State.Duration > 0)
                State.Position = State.Duration;
            StopPositionTimer_NoLock();
        }
        try { OnCompletion?.Invoke(); } catch { /* ignore listener failures */ }
    }

    private void HandleExited(IMpvPlayerSession session, TrackSnapshot track, string quality, ResolvedAudioStream stream)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session) || _completionRaised != 0) return;
            StopSession_NoLock();
            _bridge.InvalidateAudioStream(track.YouTubeId, quality, stream);
            State.IsPlaying = false;
            State.Error = new PlayerError(PlayerErrorKind.SourceUnavailable);
        }
    }

    private void StartPositionTimer_NoLock()
    {
        StopPositionTimer_NoLock();
        _positionTimer = new Timer(_ => PollPosition(), null, 200, 200);
    }

    private void StopPositionTimer_NoLock()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }

    internal void PollPosition()
    {
        IMpvPlayerSession? session;
        int seekGeneration;
        lock (_gate)
        {
            if (!State.IsPlaying) return;
            session = _session;
            seekGeneration = _seekGeneration;
        }
        if (session is null || !session.IsAlive) return;

        // Keep the displayed position when the player cannot supply a fresh sample.
        double? pos = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            pos = session.GetTimePosAsync(timeout.Token).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            pos = null;
        }

        if (pos is not { } seconds) return;

        lock (_gate)
        {
            if (!State.IsPlaying || !ReferenceEquals(_session, session) || seekGeneration != _seekGeneration) return;
            State.Position = Math.Max(0, seconds);
            // Never use wall-clock to decide completion while the player is running.
            // EOF is signaled by mpv end-file.
        }
    }

    private static int VolumeToMpv(float volume01) => (int)Math.Round(Math.Clamp(volume01, 0f, 1f) * 100f);
}
