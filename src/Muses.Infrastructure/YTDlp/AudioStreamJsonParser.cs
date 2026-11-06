using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Muses.Infrastructure.YTDlp;

internal static class AudioStreamJsonParser
{
    internal const int MaximumBytes = 8 * 1024 * 1024;

    internal static ResolvedAudioStream Parse(string json, string videoId, DateTimeOffset now)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw InvalidRepresentation();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ReadString(root, "id") != videoId || IsMerged(root))
                throw InvalidRepresentation();

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ReadHeaders(root, headers);
            var selected = root;
            if (root.TryGetProperty("requested_downloads", out var downloads) &&
                downloads.ValueKind != JsonValueKind.Null)
            {
                if (downloads.ValueKind != JsonValueKind.Array || downloads.GetArrayLength() != 1 ||
                    downloads[0].ValueKind != JsonValueKind.Object || IsMerged(downloads[0]))
                    throw InvalidRepresentation();
                selected = downloads[0];
                ReadHeaders(selected, headers);
            }

            var codec = ReadString(selected, "acodec");
            var rawUrl = ReadString(selected, "url");
            if (string.IsNullOrWhiteSpace(codec) || codec == "none" ||
                ReadString(selected, "vcodec") is not (null or "none") ||
                string.IsNullOrWhiteSpace(ReadString(selected, "format_id")) ||
                rawUrl is null || Encoding.UTF8.GetByteCount(rawUrl) > 32_768 ||
                !Uri.TryCreate(rawUrl, UriKind.Absolute, out var url) ||
                url.Scheme is not ("https" or "http") || url.Host.Length == 0 || url.UserInfo.Length != 0 ||
                (ReadString(selected, "protocol") ?? url.Scheme) is not ("http" or "https" or "m3u8" or "m3u8_native"))
                throw InvalidRepresentation();

            var stream = new ResolvedAudioStream(url, headers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                ReadExpiry(url));
            return stream.IsExpiring(now) ? throw InvalidRepresentation() : stream;
        }
        catch (JsonException)
        {
            throw InvalidRepresentation();
        }
    }

    internal static DateTimeOffset? ReadExpiry(Uri url)
    {
        if (!url.IsAbsoluteUri)
            return null;

        foreach (var item in url.Query.TrimStart('?').Split('&'))
        {
            if (item.StartsWith("expire=", StringComparison.Ordinal) &&
                long.TryParse(item.AsSpan("expire=".Length), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var seconds) &&
                seconds is >= -62135596800 and <= 253402300799)
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        return null;
    }

    private static bool IsMerged(JsonElement format) =>
        format.TryGetProperty("requested_formats", out var formats) && formats.ValueKind != JsonValueKind.Null;

    private static string? ReadString(JsonElement format, string name)
    {
        if (!format.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw InvalidRepresentation();
        return value.GetString();
    }

    private static void ReadHeaders(JsonElement format, Dictionary<string, string> headers)
    {
        if (!format.TryGetProperty("http_headers", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return;

        foreach (var field in fields.EnumerateObject())
        {
            var name = field.Name.ToLowerInvariant();
            if (name is not ("user-agent" or "accept" or "accept-language" or "accept-encoding" or "referer" or "origin"))
                continue;
            headers.Remove(name);
            if (field.Value.ValueKind != JsonValueKind.String)
                continue;
            var value = field.Value.GetString()!;
            if (Encoding.UTF8.GetByteCount(value) > 4096 || value.Any(c => c < ' ' || c == '\u007f'))
                continue;
            if (name is "referer" or "origin" &&
                (!Uri.TryCreate(value, UriKind.Absolute, out var reference) || reference.Scheme != "https" ||
                 reference.Host is not ("www.youtube.com" or "music.youtube.com" or "youtube.com") ||
                 reference.UserInfo.Length != 0 || reference.Query.Length != 0 || reference.Fragment.Length != 0))
                continue;
            headers[name] = value;
        }
    }

    private static YTDlpException InvalidRepresentation() => new("Invalid selected audio representation");
}
