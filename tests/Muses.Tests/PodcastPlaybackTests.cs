using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Playback;
using Muses.Core.Podcasts;
using Muses.Core.Queue;
using Muses.Infrastructure.Playback;
using Muses.Persistence;

namespace Muses.Tests;

public sealed class PodcastPlaybackTests
{
    private const string ShowId = "browse:MPSPshow";
    private const string VideoId = "abcdefghijk";

    [Fact]
    public async Task Pause_and_backward_seek_survive_queue_restore_and_open_paused()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var track = AddEpisode(store, library);
        var queue = new QueueService { Store = store };
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, queue, library, podcastProgress: new PodcastProgressService(store));
        playback.PlayTrack(track, [track], QueueSource.Podcast);

        engine.State.Position = 75;
        playback.Pause();
        Assert.Equal(75_000, store.Episode(VideoId)!.LastPositionMs);
        playback.Seek(40);
        playback.StopForShutdown();
        queue.Persist();

        var restoredQueue = new QueueService { Store = store };
        restoredQueue.Restore();
        var restoredEngine = new FakePlayerEngine();
        var restored = new PlaybackService(restoredEngine, restoredQueue, library,
            podcastProgress: new PodcastProgressService(store));
        await restored.RestoreCurrentPausedAsync();

        Assert.Equal(QueueSource.Podcast, restoredQueue.Current()?.FromContext);
        Assert.Equal(TrackMediaKind.PodcastEpisode, restoredEngine.LastLoadedTrack?.MediaKind);
        Assert.Equal(40, restoredEngine.State.Position);
        Assert.False(restoredEngine.State.IsPlaying);
        Assert.False(library.TrackByYouTubeId(VideoId)!.LibraryMember);
    }

    [Fact]
    public void Duplicate_end_events_leave_completion_sticky_and_replay_starts_at_zero()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var library = new LibraryService(store);
        var track = AddEpisode(store, library);
        var engine = new FakePlayerEngine();
        var progress = new PodcastProgressService(store);
        var playback = new PlaybackService(engine, new QueueService(), library, podcastProgress: progress);
        playback.PlayTrack(track, [track], QueueSource.Podcast);
        engine.State.Position = 160;

        engine.Complete();
        var played = store.Episode(VideoId)!;
        engine.Complete();

        Assert.True(played.Completed);
        Assert.Equal(played, store.Episode(VideoId));
        playback.Play();
        Assert.Equal(0, engine.State.Position);
        engine.State.Position = 30;
        playback.Pause();
        Assert.Equal(played, store.Episode(VideoId));
        progress.SetPlayed(VideoId, false);
        Assert.False(store.Episode(VideoId)!.Completed);
        Assert.Equal(0, store.Episode(VideoId)!.LastPositionMs);
    }

    private static TrackSnapshot AddEpisode(SqliteStore store, LibraryService library)
    {
        store.SaveEpisodes(ShowId, [new PodcastEpisode(VideoId, "Episode", DurationMs: 300_000)]);
        return library.UpsertFromYouTube(VideoId, "Episode", "Show", 300, null,
            mediaKind: TrackMediaKind.PodcastEpisode);
    }
}
