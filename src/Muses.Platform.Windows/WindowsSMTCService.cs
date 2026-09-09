using System.Linq.Expressions;
using System.Reflection;
using Muses.Core.Domain;
using Muses.Core.Platform;
using Muses.Core.Playback;

namespace Muses.Platform.Windows;

/// <summary>
/// System Media Transport Controls bridge.
/// On Windows 11: updates title/artist/artwork/state/timeline and routes Play/Pause/Next/Prev to PlaybackService.
/// On macOS/Linux: IsSupported=false and methods are no-ops so the app runs for development.
/// </summary>
public sealed class WindowsSMTCService : IPlatformSMTC, IDisposable
{
    private readonly PlaybackService? _playback;
    private bool _disposed;
    private IWindowsSmtcBackend _backend;

    public bool IsSupported => OperatingSystem.IsWindows() && _backend.IsAvailable;

    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public void RaisePlay() => PlayRequested?.Invoke();
    public void RaisePause() => PauseRequested?.Invoke();
    public void RaiseNext() => NextRequested?.Invoke();
    public void RaisePrevious() => PreviousRequested?.Invoke();

    public WindowsSMTCService(PlaybackService? playback = null, IWindowsSmtcBackend? backend = null)
    {
        _playback = playback;
        _backend = backend
            ?? (OperatingSystem.IsWindows() ? (IWindowsSmtcBackend?)WindowsSmtcBackend.TryCreate() : null)
            ?? NullWindowsSmtcBackend.Instance;

        PlayRequested += OnPlayRequested;
        PauseRequested += OnPauseRequested;
        NextRequested += OnNextRequested;
        PreviousRequested += OnPreviousRequested;

        _backend.PlayPressed += () => RaisePlay();
        _backend.PausePressed += () => RaisePause();
        _backend.NextPressed += () => RaiseNext();
        _backend.PreviousPressed += () => RaisePrevious();

        if (_playback is not null)
            _playback.EventBus.EventPosted += OnPlaybackEvent;
    }

    private void OnPlayRequested() => _playback?.Play();
    private void OnPauseRequested() => _playback?.Pause();
    private void OnNextRequested() => _playback?.Next();
    private void OnPreviousRequested() => _playback?.Previous();

    private void OnPlaybackEvent(PlaybackEvent evt)
    {
        if (_playback is null) return;
        UpdateTrack(_playback.State.Track, _playback.State.IsPlaying);
        UpdatePosition(_playback.State.Position, _playback.State.Track?.DurationSeconds ?? 0);
    }

    public void UpdateTrack(TrackSnapshot? track, bool isPlaying)
    {
        if (!IsSupported) return;
        _backend.UpdateDisplay(track?.Title, track?.Artist, track?.ArtworkUrl, isPlaying);
    }

    public void UpdatePosition(double positionSeconds, double durationSeconds)
    {
        if (!IsSupported) return;
        _backend.UpdateTimeline(positionSeconds, durationSeconds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_playback is not null)
            _playback.EventBus.EventPosted -= OnPlaybackEvent;
        _backend.Dispose();
    }
}

public interface IWindowsSmtcBackend : IDisposable
{
    bool IsAvailable { get; }
    event Action? PlayPressed;
    event Action? PausePressed;
    event Action? NextPressed;
    event Action? PreviousPressed;
    void UpdateDisplay(string? title, string? artist, string? artworkUrl, bool isPlaying);
    void UpdateTimeline(double positionSeconds, double durationSeconds);
}

public sealed class NullWindowsSmtcBackend : IWindowsSmtcBackend
{
    public static NullWindowsSmtcBackend Instance { get; } = new();
    public bool IsAvailable => false;
    public event Action? PlayPressed { add { } remove { } }
    public event Action? PausePressed { add { } remove { } }
    public event Action? NextPressed { add { } remove { } }
    public event Action? PreviousPressed { add { } remove { } }
    public void UpdateDisplay(string? title, string? artist, string? artworkUrl, bool isPlaying) { }
    public void UpdateTimeline(double positionSeconds, double durationSeconds) { }
    public void Dispose() { }
}

/// <summary>
/// Windows SMTC backend. Uses Windows Runtime SystemMediaTransportControls when the OS APIs are present.
/// Compiled for all TFMs; activation is runtime-gated so macOS/Linux builds stay no-op.
/// Prefer MediaPlayer.SystemMediaTransportControls (works for unpackaged desktop) over GetForCurrentView.
/// </summary>
public sealed class WindowsSmtcBackend : IWindowsSmtcBackend
{
    private readonly object? _controls;
    private readonly object? _updater;
    private readonly object? _mediaPlayerKeepAlive;
    private Delegate? _buttonHandler;
    private bool _disposed;

    public bool IsAvailable => _controls is not null;
    public event Action? PlayPressed;
    public event Action? PausePressed;
    public event Action? NextPressed;
    public event Action? PreviousPressed;

    private WindowsSmtcBackend(object controls, object updater, object? mediaPlayerKeepAlive)
    {
        _controls = controls;
        _updater = updater;
        _mediaPlayerKeepAlive = mediaPlayerKeepAlive;
        TryHookButtons(controls);
    }

    internal void EmitPlay() => PlayPressed?.Invoke();
    internal void EmitPause() => PausePressed?.Invoke();
    internal void EmitNext() => NextPressed?.Invoke();
    internal void EmitPrevious() => PreviousPressed?.Invoke();

    public static WindowsSmtcBackend? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var fromPlayer = TryCreateFromMediaPlayer();
            if (fromPlayer is not null) return fromPlayer;

            var smtcType = ResolveType(
                "Windows.Media.SystemMediaTransportControls, Windows.Media, ContentType=WindowsRuntime",
                "Windows.Media.SystemMediaTransportControls, Microsoft.Windows.SDK.NET");
            if (smtcType is null) return null;

            var getForCurrentView = smtcType.GetMethod("GetForCurrentView", Type.EmptyTypes);
            var controls = getForCurrentView?.Invoke(null, null);
            if (controls is null) return null;

            var updater = controls.GetType().GetProperty("DisplayUpdater")?.GetValue(controls);
            if (updater is null) return null;

            EnableTransport(controls);
            return new WindowsSmtcBackend(controls, updater, null);
        }
        catch
        {
            return null;
        }
    }

    private static WindowsSmtcBackend? TryCreateFromMediaPlayer()
    {
        try
        {
            var playerType = ResolveType(
                "Windows.Media.Playback.MediaPlayer, Windows.Media, ContentType=WindowsRuntime",
                "Windows.Media.Playback.MediaPlayer, Microsoft.Windows.SDK.NET");
            if (playerType is null) return null;

            var player = Activator.CreateInstance(playerType);
            if (player is null) return null;

            try
            {
                var cmdMgr = playerType.GetProperty("CommandManager")?.GetValue(player);
                if (cmdMgr is not null)
                    TrySet(cmdMgr, "IsEnabled", false);
            }
            catch { /* optional */ }

            var controls = playerType.GetProperty("SystemMediaTransportControls")?.GetValue(player);
            if (controls is null) return null;
            var updater = controls.GetType().GetProperty("DisplayUpdater")?.GetValue(controls);
            if (updater is null) return null;

            EnableTransport(controls);
            return new WindowsSmtcBackend(controls, updater, player);
        }
        catch
        {
            return null;
        }
    }

    private static void EnableTransport(object controls)
    {
        TrySet(controls, "IsEnabled", true);
        TrySet(controls, "IsPlayEnabled", true);
        TrySet(controls, "IsPauseEnabled", true);
        TrySet(controls, "IsNextEnabled", true);
        TrySet(controls, "IsPreviousEnabled", true);
    }

    private void TryHookButtons(object controls)
    {
        try
        {
            var evt = controls.GetType().GetEvent("ButtonPressed");
            if (evt?.EventHandlerType is null || evt.AddMethod is null) return;

            var invoke = evt.EventHandlerType.GetMethod("Invoke");
            if (invoke is null) return;
            var parms = invoke.GetParameters();
            if (parms.Length != 2) return;

            var senderParam = Expression.Parameter(parms[0].ParameterType, "sender");
            var argsParam = Expression.Parameter(parms[1].ParameterType, "args");
            var self = Expression.Constant(this);
            var method = typeof(WindowsSmtcBackend).GetMethod(
                nameof(OnButtonPressedCore),
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method is null) return;

            var call = Expression.Call(self, method, Expression.Convert(argsParam, typeof(object)));
            var lambda = Expression.Lambda(evt.EventHandlerType, call, senderParam, argsParam);
            _buttonHandler = lambda.Compile();
            evt.AddEventHandler(controls, _buttonHandler);
        }
        catch
        {
            // Projection mismatch — Raise* from tests still exercises PlaybackService wiring.
        }
    }

    private void OnButtonPressedCore(object args)
    {
        try
        {
            var button = args.GetType().GetProperty("Button")?.GetValue(args);
            var name = button?.ToString() ?? "";
            switch (name)
            {
                case "Play":
                    EmitPlay();
                    break;
                case "Pause":
                    EmitPause();
                    break;
                case "Next":
                    EmitNext();
                    break;
                case "Previous":
                    EmitPrevious();
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    public void UpdateDisplay(string? title, string? artist, string? artworkUrl, bool isPlaying)
    {
        if (_controls is null || _updater is null) return;
        try
        {
            TrySet(_controls, "PlaybackStatus", isPlaying ? 3 /*Playing*/ : 4 /*Paused*/);
            TrySet(_updater, "Type", 1);
            var musicProps = _updater.GetType().GetProperty("MusicProperties")?.GetValue(_updater);
            if (musicProps is not null)
            {
                TrySet(musicProps, "Title", title ?? "");
                TrySet(musicProps, "Artist", artist ?? "");
            }

            TrySetArtwork(_updater, artworkUrl);
            _updater.GetType().GetMethod("Update", Type.EmptyTypes)?.Invoke(_updater, null);
        }
        catch
        {
            // Best-effort on Windows; ignore projection mismatches.
        }
    }

    private static void TrySetArtwork(object updater, string? artworkUrl)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl)) return;
        if (!Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri)) return;
        try
        {
            var streamRefType = ResolveType(
                "Windows.Storage.Streams.RandomAccessStreamReference, Windows.Storage, ContentType=WindowsRuntime",
                "Windows.Storage.Streams.RandomAccessStreamReference, Microsoft.Windows.SDK.NET");
            if (streamRefType is null) return;
            var create = streamRefType.GetMethod("CreateFromUri", [typeof(Uri)])
                         ?? streamRefType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .FirstOrDefault(m => m.Name == "CreateFromUri" && m.GetParameters().Length == 1);
            if (create is null) return;
            var thumb = create.Invoke(null, [uri]);
            TrySet(updater, "Thumbnail", thumb);
        }
        catch
        {
            // Artwork is best-effort.
        }
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        if (_controls is null) return;
        try
        {
            var timelineType = ResolveType(
                "Windows.Media.SystemMediaTransportControlsTimelineProperties, Windows.Media, ContentType=WindowsRuntime",
                "Windows.Media.SystemMediaTransportControlsTimelineProperties, Microsoft.Windows.SDK.NET");
            if (timelineType is null) return;
            var timeline = Activator.CreateInstance(timelineType);
            if (timeline is null) return;
            TrySet(timeline, "StartTime", TimeSpan.Zero);
            TrySet(timeline, "Position", TimeSpan.FromSeconds(Math.Max(0, positionSeconds)));
            TrySet(timeline, "EndTime", TimeSpan.FromSeconds(Math.Max(0, durationSeconds)));
            TrySet(timeline, "MinSeekTime", TimeSpan.Zero);
            TrySet(timeline, "MaxSeekTime", TimeSpan.FromSeconds(Math.Max(0, durationSeconds)));
            _controls.GetType().GetMethod("UpdateTimelineProperties")?.Invoke(_controls, [timeline]);
        }
        catch
        {
            // ignore
        }
    }

    private static Type? ResolveType(params string[] names)
    {
        foreach (var n in names)
        {
            var t = Type.GetType(n);
            if (t is not null) return t;
        }
        return null;
    }

    private static void TrySet(object target, string name, object? value)
    {
        var prop = target.GetType().GetProperty(name);
        if (prop is null || !prop.CanWrite) return;
        try
        {
            if (value is int i && prop.PropertyType.IsEnum)
                prop.SetValue(target, Enum.ToObject(prop.PropertyType, i));
            else
                prop.SetValue(target, value);
        }
        catch
        {
            // ignore type mismatches across SDK versions
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_controls is not null && _buttonHandler is not null)
            {
                var evt = _controls.GetType().GetEvent("ButtonPressed");
                evt?.RemoveEventHandler(_controls, _buttonHandler);
            }
        }
        catch { /* ignore */ }
        try { TrySet(_controls!, "IsEnabled", false); } catch { /* ignore */ }
        try
        {
            if (_mediaPlayerKeepAlive is IDisposable d)
                d.Dispose();
            else
                _mediaPlayerKeepAlive?.GetType().GetMethod("Dispose")?.Invoke(_mediaPlayerKeepAlive, null);
        }
        catch { /* ignore */ }
    }
}