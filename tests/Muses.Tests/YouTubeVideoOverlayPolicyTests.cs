using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Preferences;
using Muses.Core.Queue;
using Muses.Core.YouTube;
using Muses.Infrastructure.Playback;

namespace Muses.Tests;

public class YouTubeVideoOverlayPolicyTests
{
    private static (PlaybackService playback, FakePlayerEngine engine, FakeYouTubeIFrameClient iframe, YouTubeVideoOverlaySession session)
        Create(bool resumeAfterVideo = true)
    {
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        var iframe = new FakeYouTubeIFrameClient();
        var prefs = new MemoryPreferences();
        prefs.SetBool(PrefKey.ResumeAfterVideo, resumeAfterVideo);
        var session = new YouTubeVideoOverlaySession(playback, iframe, prefs);
        return (playback, engine, iframe, session);
    }

    [Fact]
    public async Task Open_pauses_and_suspends_native_and_loads_iframe()
    {
        var (playback, engine, iframe, session) = Create();
        var track = TrackSnapshot.Test("a", "dQw4w9WgXcQ");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);
        Assert.True(engine.State.IsPlaying);

        Assert.True(session.Open(track.YouTubeId!));
        Assert.True(session.IsOpen);
        Assert.False(engine.State.IsPlaying);
        Assert.True(engine.PauseCallCount >= 1);
        Assert.True(iframe.IsLoaded);
        Assert.Equal("dQw4w9WgXcQ", iframe.LastVideoId);
        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", iframe.LastHtml);
        Assert.Contains("autoplay=1", iframe.LastHtml);
        Assert.Contains("enablejsapi=1", iframe.LastHtml);

        playback.Play();
        Assert.False(engine.State.IsPlaying);
    }

    [Fact]
    public async Task Close_without_resume_flag_does_not_Play_but_tears_down()
    {
        var (playback, engine, iframe, session) = Create(resumeAfterVideo: false);
        var track = TrackSnapshot.Test("a", "abc123_-xy");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);
        session.Open(track.YouTubeId!);
        var playsBeforeClose = engine.PlayCallCount;

        session.Close();

        Assert.False(session.IsOpen);
        Assert.False(iframe.IsLoaded);
        Assert.Equal(1, iframe.TearDownCount);
        Assert.Equal(playsBeforeClose, engine.PlayCallCount);
        Assert.False(engine.State.IsPlaying);
    }

    [Fact]
    public async Task Close_with_resume_flag_does_Play_after_teardown()
    {
        var (playback, engine, iframe, session) = Create(resumeAfterVideo: true);
        var track = TrackSnapshot.Test("a", "abc123_-xy");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);
        session.Open(track.YouTubeId!);
        Assert.False(engine.State.IsPlaying);

        session.Close();

        Assert.Equal(1, iframe.TearDownCount);
        Assert.False(iframe.IsLoaded);
        Assert.True(engine.State.IsPlaying);
        Assert.True(engine.PlayCallCount >= 1);
    }

    [Fact]
    public async Task Close_requires_teardown_of_fake_iframe_client()
    {
        var (playback, engine, iframe, session) = Create();
        var track = TrackSnapshot.Test("a", "zzzz1111");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);
        session.Open(track.YouTubeId!);
        Assert.True(iframe.IsLoaded);
        Assert.Equal(0, iframe.TearDownCount);

        session.Close();

        Assert.Equal(1, iframe.TearDownCount);
        Assert.False(iframe.IsLoaded);
    }

    [Fact]
    public void PageHtml_sanitizes_id_and_matches_muses_contract()
    {
        var html = YouTubeEmbed.PageHtml("ab<script>1</script>c");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("youtube-nocookie.com/embed/abscript1scriptc", html);
        Assert.Contains("rel=0", html);
        Assert.Contains("modestbranding=1", html);
    }
}
