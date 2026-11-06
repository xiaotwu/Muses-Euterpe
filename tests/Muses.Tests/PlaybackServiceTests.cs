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
    public void PlayQueueItem_uses_occurrence_id_and_preserves_remaining_queue()
    {
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);
        var first = TrackSnapshot.Test("first");
        var duplicate = TrackSnapshot.Test("duplicate");
        var last = TrackSnapshot.Test("last");
        playback.PlayTrack(first, [first, duplicate, duplicate, last], QueueSource.Playlist);
        queue.AddToQueue(duplicate);
        queue.AddToQueue(duplicate);
        var originalCollection = queue.Items.Select(item => item.Id).ToArray();
        var firstInsertion = queue.UpNext[0];
        var secondInsertion = queue.UpNext[1];

        playback.PlayQueueItem(Guid.NewGuid());
        Assert.Equal(1, engine.LoadCallCount);

        playback.PlayQueueItem(originalCollection[2]);
        Assert.Equal(originalCollection[2], queue.Current()?.Id);
        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal(new[] { firstInsertion.Id, secondInsertion.Id }, queue.UpNext.Select(item => item.Id));

        playback.PlayQueueItem(secondInsertion.Id);
        Assert.Equal(secondInsertion.Id, queue.Current()?.Id);
        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal(firstInsertion.Id, Assert.Single(queue.UpNext).Id);
        Assert.Equal(originalCollection, queue.Items.Select(item => item.Id));

        engine.Complete();
        Assert.Equal(firstInsertion.Id, queue.Current()?.Id);
        engine.Complete();
        Assert.Equal(last.Id, engine.LastLoadedTrack?.Id);
        Assert.Equal(originalCollection[3], queue.Current()?.Id);
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
    public async Task Paused_recovery_seeks_without_recording_a_start_until_play()
    {
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var track = TrackSnapshot.Test("recovered", durationSeconds: 60);
        queue.Play(track, [track], QueueSource.Songs);
        queue.CheckpointPosition(track.Id, 18000);
        var playback = new PlaybackService(engine, queue);
        var events = new List<PlaybackEventKind>();
        playback.EventBus.EventPosted += evt => events.Add(evt.Kind);

        await playback.RestoreCurrentPausedAsync();
        Assert.Equal(18, engine.State.Position);
        Assert.False(engine.State.IsPlaying);
        Assert.DoesNotContain(PlaybackEventKind.TrackStarted, events);
        Assert.Equal(0, engine.PlayCallCount);

        playback.Play();
        playback.Seek(21);
        playback.Pause();

        Assert.Equal(21000, queue.LastPositionMs);
        Assert.Equal(new[] { PlaybackEventKind.TrackLoading, PlaybackEventKind.TrackReady, PlaybackEventKind.TrackStarted,
            PlaybackEventKind.TrackSeeked, PlaybackEventKind.TrackPaused }, events);
    }

    [Fact]
    public void PlayTrack_starts_at_the_selected_duplicate_playlist_occurrence()
    {
        var queue = new QueueService();
        var playback = new PlaybackService(new FakePlayerEngine(), queue);
        var track = TrackSnapshot.Test("duplicate");
        var other = TrackSnapshot.Test("other");

        playback.PlayTrack(track, [track, other, track], QueueSource.Playlist, selectedIndex: 2);

        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal(queue.Items[2].Id, queue.Current()?.Id);
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

    [Fact]
    public async Task SuspendNative_pauses_and_blocks_Play_until_ResumeNative()
    {
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        var track = TrackSnapshot.Test("a");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);
        Assert.True(engine.State.IsPlaying);

        var token = Guid.NewGuid();
        var playsBeforeSuspend = engine.PlayCallCount;
        playback.SuspendNative(token);
        Assert.False(engine.State.IsPlaying);
        Assert.Equal(1, engine.PauseCallCount);

        playback.Play();
        Assert.False(engine.State.IsPlaying); // refused while suspended

        playback.ResumeNative(token);
        Assert.True(engine.State.IsPlaying);
        Assert.Equal(playsBeforeSuspend + 1, engine.PlayCallCount);
    }

    [Fact]
    public async Task Nested_SuspendNative_requires_all_tokens_released()
    {
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        var track = TrackSnapshot.Test("a");
        playback.PlayTrack(track, [track], QueueSource.Songs);
        await Task.Delay(20);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        playback.SuspendNative(a);
        playback.SuspendNative(b);
        playback.ResumeNative(a);
        Assert.False(engine.State.IsPlaying);
        playback.Play();
        Assert.False(engine.State.IsPlaying);
        playback.ResumeNative(b);
        Assert.True(engine.State.IsPlaying);
    }
}
