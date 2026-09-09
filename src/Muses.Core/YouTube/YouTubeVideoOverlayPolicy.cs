using Muses.Core.Playback;
using Muses.Core.Preferences;

namespace Muses.Core.YouTube;

/// <summary>
/// Fakeable iframe surface so overlay open/close policy can be tested without a live WebView.
/// </summary>
public interface IYouTubeIFrameClient
{
    bool IsAvailable { get; }
    bool IsLoaded { get; }
    void Load(string videoId, string pageHtml);
    void TearDown();
}

/// <summary>
/// Test double that records load/teardown without hosting a real WebView.
/// </summary>
public sealed class FakeYouTubeIFrameClient : IYouTubeIFrameClient
{
    public bool IsAvailable { get; set; } = true;
    public bool IsLoaded { get; private set; }
    public string? LastVideoId { get; private set; }
    public string? LastHtml { get; private set; }
    public int LoadCount { get; private set; }
    public int TearDownCount { get; private set; }

    public void Load(string videoId, string pageHtml)
    {
        LastVideoId = videoId;
        LastHtml = pageHtml;
        IsLoaded = true;
        LoadCount++;
    }

    public void TearDown()
    {
        IsLoaded = false;
        LastVideoId = null;
        LastHtml = null;
        TearDownCount++;
    }
}

/// <summary>
/// Orchestrates native pause/suspend on open and teardown + optional Play on close.
/// PlaybackService remains the only UI playback facade; this policy never talks to yt-dlp.
/// </summary>
public sealed class YouTubeVideoOverlaySession
{
    private readonly PlaybackService _playback;
    private readonly IYouTubeIFrameClient _iframe;
    private readonly IPreferences _preferences;
    private Guid? _suspensionToken;
    private bool _wasPlayingBeforeOpen;
    private bool _isOpen;

    public YouTubeVideoOverlaySession(
        PlaybackService playback,
        IYouTubeIFrameClient iframe,
        IPreferences? preferences = null)
    {
        _playback = playback;
        _iframe = iframe;
        _preferences = preferences ?? MemoryPreferences.Empty;
    }

    public bool IsOpen => _isOpen;
    public bool EmbedAvailable => _iframe.IsAvailable;
    public bool EmbedLoaded => _iframe.IsLoaded;
    public Guid? SuspensionToken => _suspensionToken;

    public bool ResumeAfterVideo
    {
        get => _preferences.GetBool(PrefKey.ResumeAfterVideo, true);
        set => _preferences.SetBool(PrefKey.ResumeAfterVideo, value);
    }

    /// <summary>
    /// Open overlay for <paramref name="videoId"/>. If native audio is playing,
    /// Pause + SuspendNative so the iframe owns sound while open.
    /// </summary>
    public bool Open(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return false;
        if (_isOpen) Close();

        _wasPlayingBeforeOpen = _playback.State.IsPlaying;
        if (_wasPlayingBeforeOpen)
            _playback.Pause();

        var token = Guid.NewGuid();
        _playback.SuspendNative(token);
        _suspensionToken = token;

        if (_iframe.IsAvailable)
            _iframe.Load(videoId, YouTubeEmbed.PageHtml(videoId));

        _isOpen = true;
        return true;
    }

    /// <summary>
    /// Close overlay: tear down iframe (required), ResumeNative, then Play only when
    /// ResumeAfterVideo is true and native was playing when the overlay opened.
    /// </summary>
    public void Close()
    {
        if (!_isOpen && _suspensionToken is null && !_iframe.IsLoaded) return;

        // Teardown is mandatory so a retained iframe cannot keep playing.
        _iframe.TearDown();

        var token = _suspensionToken;
        _suspensionToken = null;
        _isOpen = false;

        if (token is { } t)
            _playback.ResumeNative(t);

        var shouldPlay = ResumeAfterVideo && _wasPlayingBeforeOpen;
        _wasPlayingBeforeOpen = false;
        if (shouldPlay)
            _playback.Play();
    }
}
