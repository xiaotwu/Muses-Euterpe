using Muses.Core.Domain;

namespace Muses.Core.YouTube;

public static class YouTubeLinks
{
    public static YouTubeLinkKind Detect(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed) || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return YouTubeLinkKind.Unknown;

        var query = ParseQuery(uri);
        if (query.TryGetValue("list", out var list) && !string.IsNullOrEmpty(list))
            return YouTubeLinkKind.Playlist;

        var host = uri.Host.ToLowerInvariant();
        var isYouTube = host.EndsWith("youtube.com", StringComparison.Ordinal) || host == "youtu.be";
        if (!isYouTube) return YouTubeLinkKind.Unknown;

        if (host == "youtu.be" && uri.AbsolutePath.Length > 1) return YouTubeLinkKind.Video;
        if (query.TryGetValue("v", out var v) && !string.IsNullOrEmpty(v)) return YouTubeLinkKind.Video;
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.StartsWith("/shorts/", StringComparison.Ordinal) || path.StartsWith("/embed/", StringComparison.Ordinal))
            return YouTubeLinkKind.Video;
        return YouTubeLinkKind.Unknown;
    }

    public static string? ExtractVideoId(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        var query = ParseQuery(uri);
        if (query.TryGetValue("v", out var v) && !string.IsNullOrEmpty(v)) return v;

        var host = uri.Host.ToLowerInvariant();
        if (!(host.EndsWith("youtube.com", StringComparison.Ordinal) || host == "youtu.be"))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (host == "youtu.be")
            return segments.Length > 0 ? segments[0] : null;
        if (segments.Length >= 2 &&
            (segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)
             || segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase)))
            return segments[1];
        return null;
    }

    public static string? ExtractPlaylistId(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        var query = ParseQuery(uri);
        return query.TryGetValue("list", out var list) && !string.IsNullOrEmpty(list) ? list : null;
    }

    public static string ThumbnailUrl(string videoId) =>
        $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";

    public static bool IsLetterboxed(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        if (!host.Contains("ytimg.com", StringComparison.Ordinal)) return false;
        var path = url.AbsolutePath.ToLowerInvariant();
        return path.Contains("hqdefault", StringComparison.Ordinal)
               || path.Contains("sddefault", StringComparison.Ordinal)
               || path.EndsWith("/default.jpg", StringComparison.Ordinal)
               || path.EndsWith("/default.webp", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = uri.Query;
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
