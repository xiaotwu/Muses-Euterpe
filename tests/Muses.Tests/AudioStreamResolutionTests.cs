using System.Text.Json;
using Muses.Infrastructure.YTDlp;

namespace Muses.Tests;

public sealed class AudioStreamResolutionTests
{
    private const string VideoId = "jNQXAC9IVRw";
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Selected_audio_overrides_root_headers_and_filters_private_request_context()
    {
        var expiration = Now.AddMinutes(2);
        var url = $"https://example.com/audio.m4a?expire={expiration.ToUnixTimeSeconds()}";
        var json = $$"""
            {
              "id": "{{VideoId}}",
              "http_headers": {
                "User-Agent": "root",
                "Accept-Language": "en-US,en;q=0.8",
                "Origin": "https://www.youtube.com",
                "Cookie": "session=secret"
              },
              "requested_downloads": [{
                "url": "{{url}}",
                "format_id": "140",
                "acodec": "mp4a.40.2",
                "vcodec": "none",
                "protocol": "https",
                "http_headers": {
                  "USER-AGENT": "selected",
                  "Referer": "https://music.youtube.com/",
                  "Origin": "https://www.youtube.com/?account=private",
                  "Accept": "audio/*\r\nX-Injected: value",
                  "Authorization": "Bearer secret"
                }
              }]
            }
            """;

        var stream = AudioStreamJsonParser.Parse(json, VideoId, Now);

        Assert.Equal(new Uri(url), stream.Url);
        Assert.Equal(expiration, stream.ExpiresAt);
        Assert.Equal(3, stream.RequestHeaders.Count);
        Assert.Equal("selected", stream.RequestHeaders["User-Agent"]);
        Assert.Equal("en-US,en;q=0.8", stream.RequestHeaders["Accept-Language"]);
        Assert.Equal("https://music.youtube.com/", stream.RequestHeaders["Referer"]);
    }

    [Theory]
    [InlineData("wrong-id", "https://example.com/a", "none", "https", false)]
    [InlineData(VideoId, "file:///audio.m4a", "none", "file", false)]
    [InlineData(VideoId, "https://user:password@example.com/a", "none", "https", false)]
    [InlineData(VideoId, "https://example.com/a", "av1", "https", false)]
    [InlineData(VideoId, "https://example.com/a", "none", "http_dash_segments", false)]
    [InlineData(VideoId, "https://example.com/a", "none", "https", true)]
    public void Rejects_non_audio_or_unexpected_representations(
        string id, string url, string videoCodec, string protocol, bool merged)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            url,
            format_id = "140",
            acodec = "mp4a.40.2",
            vcodec = videoCodec,
            protocol,
            requested_formats = merged ? new[] { "audio", "video" } : null
        });

        var error = Assert.Throws<YTDlpException>(() => AudioStreamJsonParser.Parse(json, VideoId, Now));
        Assert.Equal("Invalid selected audio representation", error.Message);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void Signed_expiry_has_a_thirty_second_margin(int secondsUntilExpiry, bool usable)
    {
        var expire = Now.AddSeconds(secondsUntilExpiry).ToUnixTimeSeconds();
        var json = $$"""
            {
              "id": "{{VideoId}}",
              "url": "https://example.com/a?expire={{expire}}",
              "format_id": "140",
              "acodec": "mp4a.40.2"
            }
            """;

        if (usable)
            Assert.Equal(Now.AddSeconds(secondsUntilExpiry), AudioStreamJsonParser.Parse(json, VideoId, Now).ExpiresAt);
        else
            Assert.Throws<YTDlpException>(() => AudioStreamJsonParser.Parse(json, VideoId, Now));
    }

    [Fact]
    public void Audio_cache_is_scoped_by_context_and_invalidates_only_the_failed_result()
    {
        var cache = new StreamUrlCache();
        var context = new object();
        var first = new ResolvedAudioStream(new Uri("https://example.com/first"), new Dictionary<string, string>());
        var retry = new ResolvedAudioStream(new Uri("https://example.com/retry"), new Dictionary<string, string>());
        cache.SetAudio(context, VideoId, "bestaudio", first);

        Assert.Same(first, cache.GetAudio(context, VideoId, "bestaudio"));
        Assert.Null(cache.GetAudio(new object(), VideoId, "bestaudio"));
        Assert.Null(cache.GetAudio(context, VideoId, "64k"));
        Assert.False(cache.TryGet(VideoId, "bestaudio", out _));

        cache.SetAudio(context, VideoId, "bestaudio", retry);
        cache.InvalidateAudio(context, VideoId, "bestaudio", first);
        Assert.Same(retry, cache.GetAudio(context, VideoId, "bestaudio"));
        cache.InvalidateAudio(context, VideoId, "bestaudio", retry);
        Assert.Null(cache.GetAudio(context, VideoId, "bestaudio"));
    }

    [Fact]
    public void Public_error_uses_a_category_without_leaking_signed_request_details()
    {
        var category = YTDlpRunner.ErrorCategory(
            "ERROR: HTTP Error 403 opening https://example.com/audio?signature=private");
        Assert.Equal("http error 403", category);
        Assert.Equal("extraction failed", YTDlpRunner.ErrorCategory("authorization: credential"));
    }
}
