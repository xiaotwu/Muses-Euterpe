using Muses.Core.Domain;
using Muses.Core.YouTube;

namespace Muses.Tests;

public class YouTubeLinkTests
{
    [Fact]
    public void Detects_watch_url_as_video()
    {
        Assert.Equal(YouTubeLinkKind.Video, YouTubeLinks.Detect("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.Equal("dQw4w9WgXcQ", YouTubeLinks.ExtractVideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
    }

    [Fact]
    public void Detects_youtu_be_as_video()
    {
        Assert.Equal(YouTubeLinkKind.Video, YouTubeLinks.Detect("https://youtu.be/dQw4w9WgXcQ"));
        Assert.Equal("dQw4w9WgXcQ", YouTubeLinks.ExtractVideoId("https://youtu.be/dQw4w9WgXcQ"));
    }

    [Fact]
    public void Detects_shorts_and_embed()
    {
        Assert.Equal(YouTubeLinkKind.Video, YouTubeLinks.Detect("https://www.youtube.com/shorts/abc12345678"));
        Assert.Equal("abc12345678", YouTubeLinks.ExtractVideoId("https://www.youtube.com/shorts/abc12345678"));
        Assert.Equal("abc12345678", YouTubeLinks.ExtractVideoId("https://www.youtube.com/embed/abc12345678"));
    }

    [Fact]
    public void Detects_playlist_via_list_parameter()
    {
        Assert.Equal(YouTubeLinkKind.Playlist,
            YouTubeLinks.Detect("https://www.youtube.com/playlist?list=PLtest"));
        Assert.Equal("PLtest", YouTubeLinks.ExtractPlaylistId("https://www.youtube.com/playlist?list=PLtest"));
    }

    [Fact]
    public void Thumbnail_url_is_hqdefault()
    {
        Assert.Equal("https://i.ytimg.com/vi/abc/hqdefault.jpg", YouTubeLinks.ThumbnailUrl("abc"));
        Assert.True(YouTubeLinks.IsLetterboxed(new Uri("https://i.ytimg.com/vi/abc/hqdefault.jpg")));
        Assert.False(YouTubeLinks.IsLetterboxed(new Uri("https://i.ytimg.com/vi/abc/mqdefault.jpg")));
    }
}
