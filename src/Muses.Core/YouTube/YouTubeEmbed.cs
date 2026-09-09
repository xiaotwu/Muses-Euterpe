using System.Text;

namespace Muses.Core.YouTube;

/// <summary>
/// Official YouTube iframe helpers. Native audio stays on yt-dlp; this HTML is
/// picture-only and must live inside the on-demand video overlay.
/// </summary>
public static class YouTubeEmbed
{
    public static bool IsVideo(string? youTubeId) => !string.IsNullOrWhiteSpace(youTubeId);

    public static string? WatchUrl(string videoId)
    {
        var id = SanitizeId(videoId);
        return id is null ? null : $"https://www.youtube.com/watch?v={id}";
    }

    public static string? ThumbnailUrl(string videoId)
    {
        var id = SanitizeId(videoId);
        return id is null ? null : $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";
    }

    /// <summary>
    /// Matches Muses <c>YouTubeEmbed.pageHTML</c> (youtube-nocookie, autoplay, rel=0,
    /// modestbranding, playsinline, enablejsapi).
    /// </summary>
    public static string PageHtml(string videoId)
    {
        var id = SanitizeId(videoId) ?? "";
        return $$"""
            <!DOCTYPE html><html><head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
            html,body{margin:0;background:#000;height:100%;overflow:hidden}
            iframe{position:absolute;inset:0;width:100%;height:100%;border:0}
            </style></head><body>
            <iframe src="https://www.youtube-nocookie.com/embed/{{id}}?autoplay=1&rel=0&modestbranding=1&playsinline=1&enablejsapi=1"
                    allow="autoplay; encrypted-media; picture-in-picture" allowfullscreen></iframe>
            </body></html>
            """;
    }

    public static string BlankHtml() =>
        "<!doctype html><html><body style='margin:0;background:#000'></body></html>";

    public static string PauseIframeScript() => """
        (() => {
          const frame = document.querySelector('iframe');
          if (frame && frame.contentWindow) {
            frame.contentWindow.postMessage(JSON.stringify({
              event: 'command', func: 'pauseVideo', args: []
            }), '*');
          }
          document.querySelectorAll('video, audio').forEach(media => media.pause());
        })();
        """;

    public static string? SanitizeId(string? videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return null;
        var sb = new StringBuilder(videoId.Length);
        foreach (var c in videoId)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-')
                sb.Append(c);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
