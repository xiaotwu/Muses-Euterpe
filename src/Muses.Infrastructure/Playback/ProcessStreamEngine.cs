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
    private readonly string _quality;
    private readonly object _gate = new();

    private CancellationTokenSource? _loadCts;
    private IMpvPlayerSession? _session;
    private Timer? _positionTimer;
    private float _volume = 0.8f;
    private IReadOnlyList<EqBand> _eqBands = Array.Empty<EqBand>();
    private bool _suppressCompletion;
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

    public async Task LoadAsync(TrackSnapshot track, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        CancellationTokenSource loadCts;
        lock (_gate)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loadCts = loadCts;
            StopSession_NoLock(suppressCompletion: true);
            _completionRaised = 0;
            State.Track = track;
            State.Duration = track.DurationSeconds;
            State.Position = 0;
            State.Buffering = true;
            State.Error = null;
            State.IsPlaying = false;
        }

        var ct = loadCts.Token;
        try
        {
            var url = await _bridge.ResolveStreamUrlAsync(track.YouTubeId, _quality, TimeSpan.FromSeconds(25), ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (_mpv.FindBinary() is null)
            {
                lock (_gate)
                {
                    State.Buffering = false;
                    State.IsPlaying = false;
                    State.Error = new PlayerError(PlayerErrorKind.EngineStartFailed, "mpv binary not found");
                }
                throw new InvalidOperationException("mpv binary not found on PATH or under resources/.");
            }

            IMpvPlayerSession session;
            try
            {
                session = _mpv.Start(url, VolumeToMpv(_volume));
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    State.Buffering = false;
                    State.IsPlaying = false;
                    State.Error = new PlayerError(PlayerErrorKind.EngineStartFailed, ex.Message);
                }
                throw;
            }

            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                // A newer LoadAsync cancelled us after Start — drop the orphan session.
                if (ct.IsCancellationRequested || !ReferenceEquals(_loadCts, loadCts))
                {
                    session.Dispose();
                    State.Buffering = false;
                    return;
                }

                AttachSession_NoLock(session);
                State.Buffering = false;
                State.IsPlaying = true;
                StartPositionTimer_NoLock();
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loadCts, loadCts))
                    State.Buffering = false;
            }
        }
        catch (Exception ex) when (State.Error is null)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_loadCts, loadCts))
                    return;
                State.Buffering = false;
                State.IsPlaying = false;
                State.Error = new PlayerError(PlayerErrorKind.NetworkError, ex.Message);
            }
            throw;
        }
    }

    public Task PrepareAsync(TrackSnapshot track, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool PlayPrepared() => false;

    public void Play()
    {
        lock (_gate)
        {
            if (State.Track is null) return;
            if (_session is null || !_session.IsAlive) return;
            _session.SetPause(false);
            State.IsPlaying = true;
            StartPositionTimer_NoLock();
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            State.IsPlaying = false;
            StopPositionTimer_NoLock();
            _session?.SetPause(true);
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
        lock (_gate)
        {
            var clamped = Math.Clamp(time, 0, Math.Max(State.Duration, 0));
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
        var chain = BuildEqAfChain(_eqBands);
        _session?.SetAudioFilters(chain);
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
            StopSession_NoLock(suppressCompletion: true);
        }
    }

    private void AttachSession_NoLock(IMpvPlayerSession session)
    {
        _suppressCompletion = false;
        _session = session;
        session.EndOfFile += HandleEndOfFile;
        session.Exited += HandleExited;
        ApplyEq_NoLock();
    }

    private void StopSession_NoLock(bool suppressCompletion)
    {
        _suppressCompletion = suppressCompletion;
        StopPositionTimer_NoLock();
        var session = _session;
        _session = null;
        if (session is null) return;
        session.EndOfFile -= HandleEndOfFile;
        session.Exited -= HandleExited;
        try { session.Dispose(); } catch { /* ignore */ }
    }

    private void HandleEndOfFile()
    {
        RaiseCompletionOnce();
    }

    private void HandleExited()
    {
        // Exit without a prior eof (crash / kill) must not auto-advance the queue
        // unless we already observed eof. Intentional StopSession suppresses both.
        if (_suppressCompletion) return;
        // If mpv exits cleanly after eof, EndOfFile already fired. If it exits
        // while still "playing" without eof, treat as unexpected stop — no completion.
        lock (_gate)
        {
            State.IsPlaying = false;
            StopPositionTimer_NoLock();
        }
    }

    private void RaiseCompletionOnce()
    {
        if (_suppressCompletion) return;
        if (Interlocked.Exchange(ref _completionRaised, 1) != 0) return;
        lock (_gate)
        {
            State.IsPlaying = false;
            if (State.Duration > 0)
                State.Position = State.Duration;
            StopPositionTimer_NoLock();
        }
        try { OnCompletion?.Invoke(); } catch { /* ignore listener failures */ }
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

    private void PollPosition()
    {
        IMpvPlayerSession? session;
        lock (_gate)
        {
            if (!State.IsPlaying) return;
            session = _session;
        }
        if (session is null || !session.IsAlive) return;

        // Prefer a live IPC read; fall back to the session's last known time-pos.
        double? pos = null;
        try
        {
            pos = session.GetTimePosAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            pos = null;
        }

        pos ??= session.TimePos;

        lock (_gate)
        {
            if (!State.IsPlaying || !ReferenceEquals(_session, session)) return;
            State.Position = Math.Max(0, pos.Value);
            // Never use wall-clock to decide completion while the player is running.
            // EOF is signaled by mpv end-file.
        }
    }

    private static int VolumeToMpv(float volume01) => (int)Math.Round(Math.Clamp(volume01, 0f, 1f) * 100f);
}
