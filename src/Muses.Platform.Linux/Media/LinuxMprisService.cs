using System.Diagnostics;
using System.Net.Sockets;
using Muses.Core.Domain;
using Muses.Core.Platform;
using Muses.Core.Playback;
using Tmds.DBus.Protocol;

namespace Muses.Platform.Linux.Media;

/// <summary>Publishes the native audio player on the Linux session bus.</summary>
public sealed partial class LinuxMprisService : IPlatformSMTC, IDisposable
{
    internal const string BusName = "org.mpris.MediaPlayer2.muses";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    private const double MicrosecondsPerSecond = 1_000_000;

    private readonly PlaybackService _playback;
    private readonly Func<Action, Task> _dispatch;
    private readonly Action? _raise;
    private readonly Action? _quit;
    private readonly string? _busAddress;
    private readonly Guid _pausedAdvanceToken = Guid.NewGuid();
    private DBusConnection? _connection;
    private volatile bool _registered;
    private volatile bool _disposed;
    private Snapshot? _published;
    private ObjectPath? _stoppedTrack;

    public LinuxMprisService(PlaybackService playback, Func<Action, Task> dispatch, Action? raise = null, Action? quit = null)
        : this(playback, dispatch, raise, quit, OperatingSystem.IsLinux() ? DBusAddress.Session : null)
    {
    }

    internal LinuxMprisService(PlaybackService playback, Func<Action, Task> dispatch, Action? raise, Action? quit, string? busAddress)
    {
        _playback = playback;
        _dispatch = dispatch;
        _raise = raise;
        _quit = quit;
        _busAddress = busAddress;
        _playback.EventBus.EventPosted += OnPlaybackEvent;
    }

    public bool IsSupported => !_disposed && _registered;
    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public async Task StartAsync()
    {
        if (_disposed || string.IsNullOrWhiteSpace(_busAddress)) return;

        var connection = new DBusConnection(_busAddress);
        if (Interlocked.CompareExchange(ref _connection, connection, null) is not null)
        {
            connection.Dispose();
            return;
        }

        try
        {
            if (_disposed)
            {
                Release(connection);
                return;
            }

            await connection.ConnectAsync().AsTask().WaitAsync(ConnectTimeout).ConfigureAwait(false);
            connection.AddMethodHandler(new Endpoint(this));
            if (!await connection.TryRequestNameAsync(BusName, RequestNameOptions.None).WaitAsync(ConnectTimeout).ConfigureAwait(false))
            {
                // A separately launched instance must not displace the current owner.
                await connection.RequestNameAsync($"{BusName}.instance{Environment.ProcessId}", RequestNameOptions.None)
                    .WaitAsync(ConnectTimeout).ConfigureAwait(false);
            }

            _registered = true;
            _ = WatchAsync(connection);
        }
        catch (Exception error) when (error is DBusExceptionBase or SocketException or TimeoutException or ObjectDisposedException or ArgumentException)
        {
            Release(connection);
            Trace.TraceWarning("MPRIS session bus registration failed: {0}", error.GetType().Name);
        }
    }

    private async Task WatchAsync(DBusConnection connection)
    {
        try
        {
            var disconnected = connection.DisconnectedAsync();
            // Queue edits and volume changes can happen without a playback event.
            while (await Task.WhenAny(disconnected, Task.Delay(RefreshInterval)).ConfigureAwait(false) != disconnected)
                await _dispatch(PublishChanges).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or DBusExceptionBase)
        {
            Trace.TraceWarning("MPRIS session bus monitoring ended: {0}", error.GetType().Name);
        }
        finally
        {
            Release(connection);
        }
    }

    // PlaybackService owns the state. These hooks refresh its latest snapshot.
    public void UpdateTrack(TrackSnapshot? track, bool isPlaying) => Refresh();
    public void UpdatePosition(double positionSeconds, double durationSeconds) => Refresh();

    private void Refresh() => _ = DispatchUpdateAsync(PublishChanges);

    private async Task DispatchUpdateAsync(Action action)
    {
        if (!IsSupported) return;
        try
        {
            await _dispatch(action).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or DBusExceptionBase)
        {
            Trace.TraceWarning("MPRIS state update skipped: {0}", error.GetType().Name);
        }
    }

    private void OnPlaybackEvent(PlaybackEvent evt)
    {
        _ = DispatchUpdateAsync(() =>
        {
            PublishChanges();
            if (evt.Kind == PlaybackEventKind.TrackSeeked && _playback.State.Track?.Id == evt.TrackId
                && IsSupported && _connection is { } connection)
            {
                using var writer = connection.GetMessageWriter();
                writer.WriteSignalHeader(path: ObjectPath, @interface: PlayerInterface, member: "Seeked", signature: "x");
                writer.WriteInt64(Capture().Position);
                connection.TrySendMessage(writer.CreateMessage());
            }
        });
    }

    private Snapshot Capture()
    {
        var state = _playback.State;
        var queue = _playback.Queue;
        var track = state.Track;
        var current = queue.Current();
        var occurrence = track is not null && current?.Track.Id == track.Id ? current.Id : track?.Id;
        ObjectPath trackPath = occurrence is { } id ? $"{ObjectPath}/Track/{id:N}" : NoTrack;
        var duration = ToMicroseconds(state.Duration > 0 ? state.Duration : track?.DurationSeconds ?? 0);
        var position = Math.Min(ToMicroseconds(state.Position), duration > 0 ? duration : long.MaxValue);
        if (state.IsPlaying) _stoppedTrack = null;

        var stopped = track is null || _stoppedTrack == trackPath || (duration > 0 && position >= duration && !state.Buffering);
        return new Snapshot(
            track, trackPath, duration, position,
            state.IsPlaying ? "Playing" : stopped ? "Stopped" : "Paused",
            queue.RepeatMode, queue.Shuffle, _playback.Volume,
            queue.UpNext.Count > 0 || queue.CurrentIndex + 1 < queue.Items.Count
                || (queue.RepeatMode == RepeatMode.All && queue.Items.Count > 0),
            queue.History.Count > 0 || queue.CurrentIndex > 0 ||
                (queue.CurrentIndex == 0 && queue.Items.Count > 0 && current?.Id != queue.Items[0].Id),
            track is not null || current is not null,
            track is not null || current is not null,
            track is not null && duration > 0 && !state.Buffering && state.Error is null);
    }

    private void Play()
    {
        _stoppedTrack = null;
        _playback.Play();
        PlayRequested?.Invoke();
    }

    private void Pause()
    {
        _playback.Pause();
        PauseRequested?.Invoke();
    }

    private void Toggle()
    {
        var wasPlaying = _playback.State.IsPlaying;
        _stoppedTrack = null;
        _playback.Toggle();
        if (wasPlaying) PauseRequested?.Invoke();
        else PlayRequested?.Invoke();
    }

    private void Move(bool forward, string status)
    {
        var keepPaused = status != "Playing";
        if (keepPaused) _playback.SuspendNative(_pausedAdvanceToken);
        try
        {
            if (forward) _playback.Next();
            else _playback.Previous();

            if (keepPaused) _playback.Pause();
            if (status == "Stopped") _stoppedTrack = Capture().TrackPath;
        }
        finally
        {
            if (keepPaused) _playback.ResumeNative(_pausedAdvanceToken);
        }

        if (forward) NextRequested?.Invoke();
        else PreviousRequested?.Invoke();
    }

    private void Stop()
    {
        var state = Capture();
        if (state.Status == "Stopped") return;

        _stoppedTrack = state.TrackPath;
        _playback.Pause();
        if (state.Position != 0) _playback.Seek(0);
    }

    private void Seek(long offset)
    {
        var state = Capture();
        if (!state.CanSeek) return;

        // Offset can be any signed 64-bit value. Compare before adding it.
        if (offset > state.Duration - state.Position)
        {
            if (state.CanGoNext) Move(forward: true, state.Status);
            else Stop();
        }
        else
        {
            _playback.Seek(Math.Max(0, state.Position + offset) / MicrosecondsPerSecond);
        }
    }

    private void SetPosition(ObjectPath trackPath, long position)
    {
        var state = Capture();
        if (state.CanSeek && state.TrackPath == trackPath && position >= 0 && position <= state.Duration)
            _playback.Seek(position / MicrosecondsPerSecond);
    }

    private static long ToMicroseconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return 0;
        var microseconds = seconds * MicrosecondsPerSecond;
        return microseconds >= long.MaxValue ? long.MaxValue : (long)microseconds;
    }

    private void Release(DBusConnection connection)
    {
        _registered = false;
        Interlocked.CompareExchange(ref _connection, null, connection);
        connection.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playback.EventBus.EventPosted -= OnPlaybackEvent;
        Interlocked.Exchange(ref _connection, null)?.Dispose();
    }

    private sealed record Snapshot(
        TrackSnapshot? Track, ObjectPath TrackPath, long Duration, long Position, string Status,
        RepeatMode Repeat, bool Shuffle, double Volume, bool CanGoNext, bool CanGoPrevious,
        bool CanPlay, bool CanPause, bool CanSeek);
}
