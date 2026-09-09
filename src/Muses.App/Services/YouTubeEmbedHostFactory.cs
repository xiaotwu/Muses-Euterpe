using Avalonia.Controls;
using Microsoft.Web.WebView2.Core;
using Muses.Core.YouTube;

namespace Muses.App.Services;

/// <summary>
/// Creates an iframe client for the video overlay. Win11 hosts WebView2 Evergreen
/// inside the overlay only — never for Home/Search/capsule audio. macOS-dev and CI
/// get a degraded client (no live WebView).
/// </summary>
public static class YouTubeEmbedHostFactory
{
    public static IYouTubeIFrameClient CreateClient()
    {
        if (OperatingSystem.IsWindows() && WindowsWebView2YouTubeIFrameClient.TryCreate(out var win))
            return win!;

        return new DegradedYouTubeIFrameClient(
            available: false,
            reason: OperatingSystem.IsMacOS()
                ? "macOS-dev: Avalonia WebView/WebView2 host not wired for this TFM"
                : "WebView2 runtime unavailable on this platform/build");
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
/// Windows WebView2 iframe surface hosted exclusively in YouTubeVideoOverlay.EmbedHost.
/// </summary>
public sealed class WindowsWebView2YouTubeIFrameClient : IYouTubeIFrameClient
{
    private readonly WebView2NativeHost _host;
    private string? _lastVideoId;

    private WindowsWebView2YouTubeIFrameClient(WebView2NativeHost host)
    {
        _host = host;
    }

    /// <summary>NativeControlHost to attach under EmbedHost when the overlay is open.</summary>
    public Control HostControl => _host;

    public static bool TryCreate(out WindowsWebView2YouTubeIFrameClient? client)
    {
        client = null;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            // Throws / empty when Evergreen WebView2 runtime is missing.
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
                return false;

            client = new WindowsWebView2YouTubeIFrameClient(new WebView2NativeHost());
            return true;
        }
        catch
        {
            client = null;
            return false;
        }
    }

    public bool IsAvailable => true;
    public bool IsLoaded { get; private set; }

    public void Load(string videoId, string pageHtml)
    {
        _lastVideoId = videoId;
        IsLoaded = true;
        _host.NavigateToHtml(string.IsNullOrEmpty(pageHtml) ? YouTubeEmbed.PageHtml(videoId) : pageHtml);
    }

    public void TearDown()
    {
        IsLoaded = false;
        _lastVideoId = null;
        _host.NavigateBlank();
    }
}
