using Muses.Core.YouTube;

namespace Muses.App.Services;

/// <summary>
/// Creates an iframe client for the video overlay. Avalonia has no built-in
/// WebView on net10.0 + Avalonia 11.3 that restores cleanly for this Mac
/// checkout, so macOS-dev (and CI) get a degraded client. Win11 will host
/// WebView2 inside the overlay only — never for Home/Search/capsule audio.
/// </summary>
public static class YouTubeEmbedHostFactory
{
    public static IYouTubeIFrameClient CreateClient()
    {
        // Prefer an explicit WebView2 host when a future Windows package is wired.
        if (OperatingSystem.IsWindows() && WindowsWebView2YouTubeIFrameClient.TryCreate(out var win))
            return win!;

        // Honest degradation: never silently Process.Start as the only path.
        return new DegradedYouTubeIFrameClient(
            available: false,
            reason: OperatingSystem.IsMacOS()
                ? "macOS-dev: Avalonia WebView/WebView2 host not wired for this TFM"
                : "WebView host unavailable on this platform/build");
    }
}

/// <summary>
/// Records that embed was requested but no live WebView exists. TearDown is
/// still mandatory for policy tests and close-path safety.
/// </summary>
public sealed class DegradedYouTubeIFrameClient : IYouTubeIFrameClient
{
    private readonly string _reason;

    public DegradedYouTubeIFrameClient(bool available, string reason)
    {
        IsAvailable = available;
        _reason = reason;
    }

    public bool IsAvailable { get; }
    public bool IsLoaded { get; private set; }
    public string Reason => _reason;
    public string? LastVideoId { get; private set; }

    public void Load(string videoId, string pageHtml)
    {
        // Unavailable surfaces refuse to "load" a live iframe.
        if (!IsAvailable) return;
        LastVideoId = videoId;
        IsLoaded = true;
    }

    public void TearDown()
    {
        IsLoaded = false;
        LastVideoId = null;
    }
}

/// <summary>
/// Placeholder for Win11 WebView2. Returns false until a compatible Avalonia
/// WebView2 package is added for net10.0; structure is ready for Phase 2.1.
/// </summary>
public sealed class WindowsWebView2YouTubeIFrameClient : IYouTubeIFrameClient
{
    private WindowsWebView2YouTubeIFrameClient() { }

    public static bool TryCreate(out WindowsWebView2YouTubeIFrameClient? client)
    {
        // No WebView2 Avalonia package is referenced yet (net10.0 + Avalonia 11.3).
        // Keep the hook so Win11 can light up without reshaping overlay policy.
        client = null;
        return false;
    }

    public bool IsAvailable => true;
    public bool IsLoaded { get; private set; }

    public void Load(string videoId, string pageHtml)
    {
        IsLoaded = true;
    }

    public void TearDown()
    {
        IsLoaded = false;
    }
}
