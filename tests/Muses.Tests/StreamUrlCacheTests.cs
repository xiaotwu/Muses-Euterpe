using Muses.Infrastructure.YTDlp;

namespace Muses.Tests;

public class StreamUrlCacheTests
{
    [Fact]
    public void Remembers_url_until_invalidated()
    {
        var cache = new StreamUrlCache(TimeSpan.FromHours(1));
        var url = new Uri("https://example.com/a.m4a");
        cache.Set("abc", "bestaudio", url);
        Assert.True(cache.TryGet("abc", "bestaudio", out var hit));
        Assert.Equal(url, hit);
        cache.Invalidate("abc", "bestaudio");
        Assert.False(cache.TryGet("abc", "bestaudio", out _));
    }

    [Fact]
    public void Expired_entries_are_ignored()
    {
        var cache = new StreamUrlCache(TimeSpan.FromMilliseconds(1));
        cache.Set("abc", "bestaudio", new Uri("https://example.com/a.m4a"));
        Thread.Sleep(5);
        Assert.False(cache.TryGet("abc", "bestaudio", out _));
    }
}
