#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Storage.Streams;

namespace Muses.Platform.Windows;

internal sealed class WindowsSmtcBackend : IWindowsSmtcBackend
{
    private readonly SystemMediaTransportControls _controls;
    private readonly SystemMediaTransportControlsDisplayUpdater _updater;
    private (string Title, string? Artist, string? ArtworkUrl)? _metadata;
    private MediaPlaybackStatus _status = MediaPlaybackStatus.Closed;

    private WindowsSmtcBackend(SystemMediaTransportControls controls)
    {
        _controls = controls;
        _updater = controls.DisplayUpdater;
        controls.IsEnabled = false;
        controls.IsPlayEnabled = true;
        controls.IsPauseEnabled = true;
        controls.IsNextEnabled = true;
        controls.IsPreviousEnabled = true;
        controls.ButtonPressed += OnButtonPressed;
        controls.PlaybackPositionChangeRequested += OnPlaybackPositionChangeRequested;
    }

    public static WindowsSmtcBackend? TryCreate(nint windowHandle)
    {
        try
        {
            return new WindowsSmtcBackend(SystemMediaTransportControlsInterop.GetForWindow(windowHandle));
        }
        catch (Exception error) when (error is COMException or TypeInitializationException)
        {
            Trace.TraceWarning("Windows media controls registration failed (0x{0:X8}).", error.HResult);
            return null;
        }
    }

    public bool IsAvailable => true;
    public event Action? PlayPressed;
    public event Action? PausePressed;
    public event Action? NextPressed;
    public event Action? PreviousPressed;
    public event Action<double>? SeekRequested;

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                PlayPressed?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Pause:
                PausePressed?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Next:
                NextPressed?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousPressed?.Invoke();
                break;
        }
    }

    private void OnPlaybackPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args) => SeekRequested?.Invoke(args.RequestedPlaybackPosition.TotalSeconds);

    public void UpdateDisplay(string? title, string? artist, string? artworkUrl, bool isPlaying)
    {
        try
        {
            if (title is null)
            {
                if (_metadata is null) return;
                _controls.IsEnabled = false;
                _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
                _updater.ClearAll();
                _metadata = null;
                _status = MediaPlaybackStatus.Closed;
                return;
            }

            var metadata = (title, artist, artworkUrl);
            if (_metadata != metadata)
            {
                _updater.ClearAll();
                _updater.Type = MediaPlaybackType.Music;
                _updater.MusicProperties.Title = title;
                _updater.MusicProperties.Artist = artist ?? "";
                if (Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "https" or "http")
                    _updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(uri);
                _updater.Update();
                _metadata = metadata;
            }

            var status = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            if (_status != status)
            {
                _controls.PlaybackStatus = status;
                if (_status == MediaPlaybackStatus.Closed)
                    _controls.IsEnabled = true;
                _status = status;
            }
        }
        catch (COMException error)
        {
            Trace.TraceWarning("Windows media controls update failed (0x{0:X8}).", error.HResult);
        }
    }

    public void UpdateTimeline(double positionSeconds, double durationSeconds)
    {
        if (_metadata is null || !double.IsFinite(positionSeconds) ||
            !double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            durationSeconds >= TimeSpan.MaxValue.TotalSeconds)
            return;

        var duration = TimeSpan.FromSeconds(durationSeconds);
        try
        {
            _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = duration,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = duration,
                Position = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, durationSeconds))
            });
        }
        catch (COMException error)
        {
            Trace.TraceWarning("Windows media timeline update failed (0x{0:X8}).", error.HResult);
        }
    }

    public void Dispose()
    {
        try
        {
            _controls.ButtonPressed -= OnButtonPressed;
            _controls.PlaybackPositionChangeRequested -= OnPlaybackPositionChangeRequested;
            _controls.IsEnabled = false;
        }
        catch (COMException error)
        {
            Trace.TraceWarning("Windows media controls shutdown failed (0x{0:X8}).", error.HResult);
        }
    }
}
#endif
