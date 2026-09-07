using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Muses.Core.Lyrics;

namespace Muses.Infrastructure.Lyrics;

public interface ILyricsProvider
{
    Task<LyricsResult?> FetchLyricsAsync(string track, string artist, string? album = null, CancellationToken ct = default);
}

public sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _http;

    public LrclibLyricsProvider(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "Muses/1.0 (https://github.com/xiaotwu/Muses)");
        }
    }

    private sealed class LrclibResponse
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("trackName")] public string? TrackName { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        [JsonPropertyName("albumName")] public string? AlbumName { get; set; }
        [JsonPropertyName("duration")] public double? Duration { get; set; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    }

    public async Task<LyricsResult?> FetchLyricsAsync(string track, string artist, string? album = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track) || string.IsNullOrWhiteSpace(artist))
            return null;

        try
        {
            // 1. Try exact get
            var getUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(track)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrEmpty(album))
            {
                getUrl += $"&album_name={Uri.EscapeDataString(album)}";
            }

            using var resp = await _http.GetAsync(getUrl, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var data = await resp.Content.ReadFromJsonAsync<LrclibResponse>(cancellationToken: ct).ConfigureAwait(false);
                if (data is not null && (!string.IsNullOrEmpty(data.SyncedLyrics) || !string.IsNullOrEmpty(data.PlainLyrics)))
                {
                    return new LyricsResult(data.PlainLyrics, data.SyncedLyrics, LyricsSource.Lrclib);
                }
            }

            // 2. Try search fallback
            var searchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(track)}&artist_name={Uri.EscapeDataString(artist)}";
            using var searchResp = await _http.GetAsync(searchUrl, ct).ConfigureAwait(false);
            if (searchResp.IsSuccessStatusCode)
            {
                var list = await searchResp.Content.ReadFromJsonAsync<List<LrclibResponse>>(cancellationToken: ct).ConfigureAwait(false);
                var candidate = list?.FirstOrDefault(c => !string.IsNullOrEmpty(c.SyncedLyrics))
                             ?? list?.FirstOrDefault(c => !string.IsNullOrEmpty(c.PlainLyrics));

                if (candidate is not null)
                {
                    return new LyricsResult(candidate.PlainLyrics, candidate.SyncedLyrics, LyricsSource.Lrclib);
                }
            }
        }
        catch
        {
            // Best-effort lyrics retrieval; swallow network errors
        }

        return null;
    }
}
