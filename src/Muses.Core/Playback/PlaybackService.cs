using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Podcasts;
using Muses.Core.Preferences;
using Muses.Core.Queue;

namespace Muses.Core.Playback;

public sealed class PlaybackService
{
    private readonly IPlayerEngine _engine;
    private readonly IPreferences _preferences;
    private readonly PodcastProgressService? _podcastProgress;
    private ulong _loadSeq;
    private Guid? _startedTrackId;
    private Guid? _lastCompletedTrackId;
    private double? _pendingResumeMs;
    private bool _playbackRequested;
    private readonly HashSet<Guid> _nativeSuspensions = [];

    public PlaybackService(IPlayerEngine engine, QueueService queue, LibraryService? library = null,
        IPreferences? preferences = null, PodcastProgressService? podcastProgress = null)
    {
        _engine = engine;
        Queue = queue;
        Library = library;
        _preferences = preferences ?? MemoryPreferences.Empty;
        _podcastProgress = podcastProgress;
        Volume = (float)Math.Clamp(_preferences.GetDouble(PrefKey.Volume, 0.8), 0, 1);
        _engine.SetVolume(Volume);
        _engine.OnCompletion = HandleEngineCompletion;
    }

    public PlayerState State => _engine.State;
    public QueueService Queue { get; }
    public LibraryService? Library { get; }
    public PlaybackEventBus EventBus { get; } = new();
    public float Volume { get; private set; }
    public bool EngineSupportsEq => _engine.SupportsEq;

    public void SetEq(IReadOnlyList<EqBand> bands) => _engine.SetEq(bands);

    public void SetCrossfadeSeconds(double seconds) => _engine.SetCrossfadeSeconds(seconds);

    public void PlayTrack(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context, QueueSource from, int? selectedIndex = null)
    {
        _playbackRequested = true;
        _startedTrackId = null;
        Queue.Play(track, context, from, selectedIndex);
        _ = ScheduleLoadAsync(track);
    }

    public void PlayQueueItem(Guid id)
    {
        if (Queue.ActivateItem(id) is not { } item) return;

        _playbackRequested = true;
        _startedTrackId = null;
        _ = ScheduleLoadAsync(item.Track);
        EventBus.Post(new PlaybackEvent(PlaybackEventKind.QueueChanged, item.Track));
    }

    public void Toggle()
    {
        if (State.Error is null && (_playbackRequested || State.IsPlaying)) Pause();
        else Play();
    }

    public void Play()
    {
        _playbackRequested = true;
        if (_nativeSuspensions.Count > 0) return;
        if (State.Track is not { } track)
        {
            if (Queue.Current() is { } current)
                _ = ScheduleLoadAsync(current.Track, restoreQueuePosition: true);
            else
                _playbackRequested = false;
            return;
        }
        if (State.Buffering || State.IsPlaying) return;
        if (State.Error is not null || _lastCompletedTrackId == track.Id)
        {
            double? resumeMs = State.Error is not null ? _pendingResumeMs ?? State.Position * 1000 : null;
            _ = ScheduleLoadAsync(track, resumeMs);
            return;
        }
        _engine.Play();
        if (!State.IsPlaying) return;
        if (_startedTrackId == track.Id)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackResumed, track));
        else
            MarkStarted(track);
    }

    public void Pause()
    {
        var wasActive = _playbackRequested || State.IsPlaying;
        _playbackRequested = false;
        _engine.Pause();
        State.IsPlaying = false;
        CheckpointPosition();
        if (wasActive && State.Track is { } track && _startedTrackId == track.Id)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackPaused, track));
    }

    public void Next()
    {
        var item = Queue.Next();
        if (item is null)
        {
            if (Queue.RepeatMode == RepeatMode.All && Queue.Next() is { } wrapped)
            {
                _playbackRequested = true;
                _ = ScheduleLoadAsync(wrapped.Track);
                return;
            }
            _playbackRequested = false;
            _engine.Pause();
            CheckpointPodcastPosition();
            return;
        }
        _playbackRequested = true;
        _ = ScheduleLoadAsync(item.Track);
    }

    public void Previous()
    {
        var currentId = Queue.Current()?.Id;
        if (Queue.Previous() is not { } item) return;
        if (item.Id == currentId && item.Track.Id == State.Track?.Id) return;
        _playbackRequested = true;
        _ = ScheduleLoadAsync(item.Track);
    }

    public void Seek(double time)
    {
        _engine.Seek(time);
        if (State.Track is { } track)
        {
            CheckpointPosition();
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackSeeked, track, track.Id, State.Position * 1000));
        }
    }

    public void SetVolume(float value)
    {
        Volume = Math.Clamp(value, 0, 1);
        _preferences.SetDouble(PrefKey.Volume, Volume);
        _engine.SetVolume(Volume);
    }

    /// <summary>
    /// Silence native audio without clearing desired play intent. Video overlay
    /// (and similar surfaces) pass a token so nested owners cannot resume while
    /// another suspension is still active.
    /// </summary>
    public void SuspendNative(Guid token)
    {
        var wasEmpty = _nativeSuspensions.Count == 0;
        _nativeSuspensions.Add(token);
        if (!wasEmpty) return;

        var wasAudible = State.IsPlaying;
        _engine.Pause();
        State.IsPlaying = false;
        CheckpointPodcastPosition();
        if (wasAudible && State.Track is { } track && _startedTrackId == track.Id)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackPaused, track));
    }

    /// <summary>
    /// Release one native-audio suspension. When the set becomes empty and play
    /// is still requested, resume the engine idempotently.
    /// </summary>
    public void ResumeNative(Guid token)
    {
        if (!_nativeSuspensions.Remove(token)) return;
        if (_nativeSuspensions.Count > 0) return;
        if (_playbackRequested) Play();
    }

    public void CycleRepeat() => Queue.SetRepeat(RepeatModeCodec.Next(Queue.RepeatMode));
    public void ToggleShuffle() => Queue.ToggleShuffle();

    public Task RestoreCurrentPausedAsync()
    {
        if (Queue.Current() is not { } current) return Task.CompletedTask;
        _playbackRequested = false;
        return ScheduleLoadAsync(current.Track, restoreQueuePosition: true);
    }

    public void CheckpointPosition()
    {
        CheckpointPodcastPosition();
        if (State.Buffering || State.Error is not null || State.Track is not { } track) return;
        if (Queue.Current()?.Track.Id != track.Id || !double.IsFinite(State.Position)) return;
        var positionMs = _lastCompletedTrackId == track.Id ? 0 : State.Position * 1000;
        if (Queue.CurrentTrackId != track.Id || Queue.LastPositionMs != positionMs)
            Queue.CheckpointPosition(track.Id, positionMs);
    }

    public void StopForShutdown()
    {
        CheckpointPodcastPosition();
        _podcastProgress?.RetryPending();
        NotifyInterruption(PlaybackEventKind.TrackStopped);
        _loadSeq++;
        _playbackRequested = false;
        _engine.OnCompletion = null;
    }

    private double? SavedPositionMs(TrackSnapshot track) =>
        Queue.CurrentTrackId == track.Id ? Queue.LastPositionMs : null;

    private async Task ScheduleLoadAsync(TrackSnapshot track, double? resumeMs = null, bool restoreQueuePosition = false)
    {
        var seq = ++_loadSeq;
        CheckpointPodcastPosition();
        NotifyInterruption(PlaybackEventKind.TrackSkipped);
        _startedTrackId = null;
        _pendingResumeMs = resumeMs
            ?? (track.MediaKind == TrackMediaKind.PodcastEpisode ? _podcastProgress?.GetResumePositionMs(track.YouTubeId) : null)
            ?? (restoreQueuePosition ? SavedPositionMs(track) : null);
        try
        {
            var load = _engine.LoadPausedAsync(track);
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackLoading, track));
            await load.ConfigureAwait(true);
            if (seq != _loadSeq) return;
            _lastCompletedTrackId = null;
            if (_pendingResumeMs is { } ms && double.IsFinite(ms) && ms > 0)
            {
                var position = State.Duration > 0 ? Math.Min(ms / 1000, Math.Max(0, State.Duration - 2)) : ms / 1000;
                _engine.Seek(position);
            }
            _pendingResumeMs = null;
            if (_playbackRequested && _nativeSuspensions.Count == 0)
            {
                _engine.Play();
                if (State.IsPlaying) MarkStarted(track);
                else EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackReady, track));
            }
            else
            {
                _engine.Pause();
                EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackReady, track));
            }
            CheckpointPosition();
        }
        catch
        {
            if (seq != _loadSeq) return;
            State.Error ??= new PlayerError(PlayerErrorKind.SourceUnavailable);
            State.IsPlaying = false;
            _playbackRequested = false;
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackLoadFailed, track));
        }
    }

    private void NotifyInterruption(PlaybackEventKind kind)
    {
        if (State.Track is { } track && _startedTrackId == track.Id && _lastCompletedTrackId != track.Id)
            EventBus.Post(new PlaybackEvent(kind, track));
    }

    private void CheckpointPodcastPosition()
    {
        if (_podcastProgress is null || State.Buffering || State.Error is not null
            || State.Track is not { MediaKind: TrackMediaKind.PodcastEpisode } track
            || _lastCompletedTrackId == track.Id)
            return;
        _podcastProgress.Checkpoint(track, State.Position, State.Duration);
    }

    private void HandleEngineCompletion()
    {
        if (State.Track is not { } done || State.Buffering || State.Error is not null || _lastCompletedTrackId == done.Id)
            return;
        _lastCompletedTrackId = done.Id;
        if (done.MediaKind == TrackMediaKind.PodcastEpisode)
            _podcastProgress?.Complete(done, State.Position, State.Duration);
        EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackCompleted, done));

        if (Queue.RepeatMode == RepeatMode.Off
            && Queue.CurrentIndex >= Queue.Items.Count - 1
            && Queue.UpNext.Count == 0)
        {
            _playbackRequested = false;
            State.IsPlaying = false;
            CheckpointPosition();
            return;
        }

        var next = Queue.RepeatMode == RepeatMode.One && Queue.UpNext.Count == 0
            ? Queue.Current() : Queue.PeekNext();
        if (next?.Track.MediaKind != TrackMediaKind.PodcastEpisode && _engine.PlayPrepared())
        {
            _ = Queue.Next();
            _lastCompletedTrackId = null;
            if (Queue.Current()?.Track is { } started)
                MarkStarted(started);
            return;
        }

        Next();
    }

    private void MarkStarted(TrackSnapshot track)
    {
        _startedTrackId = track.Id;
        Library?.RecordPlay(track.Id);
        EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackStarted, track));
    }
}
