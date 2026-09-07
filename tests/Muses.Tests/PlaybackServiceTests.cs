using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Infrastructure.Playback;

namespace Muses.Tests;

public class PlaybackServiceTests
{
    [Fact]
    public async Task PlayTrack_loads_engine_and_sets_queue_index()
    {
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);
        var ctx = new[] { TrackSnapshot.Test("a"), TrackSnapshot.Test("b") };
        playback.PlayTrack(ctx[1], ctx, QueueSource.Search);
        await Task.Delay(20);
        Assert.Equal("b", engine.LastLoadedTrack?.Title);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.True(engine.State.IsPlaying);
        Assert.Equal(1, engine.LoadCallCount);
    }

    [Fact]
    public async Task Next_loads_the_following_collection_item()
    {
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);
        var ctx = new[] { TrackSnapshot.Test("a"), TrackSnapshot.Test("b") };
        playback.PlayTrack(ctx[0], ctx, QueueSource.Songs);
        await Task.Delay(20);
        playback.Next();
        await Task.Delay(20);
        Assert.Equal("b", engine.LastLoadedTrack?.Title);
    }

    [Fact]
    public async Task Toggle_pauses_and_resumes()
    {
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        var track = TrackSnapshot.Test("a");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);
        playback.Toggle();
        Assert.False(engine.State.IsPlaying);
        playback.Toggle();
        Assert.True(engine.State.IsPlaying);
    }

    [Fact]
    public async Task Completion_advances_queue()
    {
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);
        var ctx = new[] { TrackSnapshot.Test("a"), TrackSnapshot.Test("b") };
        playback.PlayTrack(ctx[0], ctx, QueueSource.Songs);
        await Task.Delay(20);
        engine.Complete();
        await Task.Delay(20);
        Assert.Equal("b", engine.LastLoadedTrack?.Title);
    }
}
