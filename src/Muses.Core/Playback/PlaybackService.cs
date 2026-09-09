using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Preferences;
using Muses.Core.Queue;

namespace Muses.Core.Playback;

public sealed class PlaybackService
{
    private readonly IPlayerEngine _engine;
    private readonly IPreferences _preferences;
    private ulong _loadSeq;
    private Guid? _startedTrackId;
    private Guid? _lastCompletedTrackId;
    private bool _playbackRequested;
    private readonly HashSet<Guid> _nativeSuspensions = [];

    public PlaybackService(IPlayerEngine engine, QueueService queue, LibraryService? library = null, IPreferences? preferences = null)
    {
        _engine = engine;
        Queue = queue;
        Library = library;
        _preferences = preferences ?? MemoryPreferences.Empty;
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

    public void PlayTrack(TrackSnapshot track, IReadOnlyList<TrackSnapshot> context, QueueSource from)
    {
        _playbackRequested = true;
        _startedTrackId = null;
        Queue.Play(track, context, from);
        _ = ScheduleLoadAsync(track);
    }

    public void Toggle()
    {
        if (_playbackRequested || State.IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        _playbackRequested = true;
        if (State.Track is null) return;
        if (_nativeSuspensions.Count > 0) return;
        if (State.Buffering || State.IsPlaying) return;
        _engine.Play();
        if (_startedTrackId == State.Track.Id)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackResumed, State.Track));
        else
            MarkStarted(State.Track);
    }

    public void Pause()
    {
        var wasActive = _playbackRequested || State.IsPlaying;
        _playbackRequested = false;
        _engine.Pause();
        State.IsPlaying = false;
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
            State.IsPlaying = false;
            return;
        }
        _playbackRequested = true;
        _ = ScheduleLoadAsync(item.Track);
    }

    public void Previous()
    {
        if (Queue.Previous() is not { } item) return;
        if (item.Track.Id == State.Track?.Id) return;
        _playbackRequested = true;
        _ = ScheduleLoadAsync(item.Track);
    }

    public void Seek(double time)
    {
        _engine.Seek(time);
        if (State.Track is { } track)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackSeeked, track, track.Id, time * 1000));
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
        if (!_playbackRequested) return;
        if (State.Track is null || State.Buffering || State.IsPlaying) return;
        _engine.Play();
        if (_startedTrackId == State.Track.Id)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackResumed, State.Track));
        else
            MarkStarted(State.Track);
    }

    public void CycleRepeat() => Queue.SetRepeat(RepeatModeCodec.Next(Queue.RepeatMode));
    public void ToggleShuffle() => Queue.ToggleShuffle();

    private async Task ScheduleLoadAsync(TrackSnapshot track)
    {
        var seq = ++_loadSeq;
        try
        {
            await _engine.LoadAsync(track).ConfigureAwait(true);
            if (seq != _loadSeq) return;
            if (!_playbackRequested)
            {
                _engine.Pause();
                State.IsPlaying = false;
                return;
            }
            MarkStarted(track);
            Library?.RecordPlay(track.Id);
            Queue.CheckpointPosition(track.Id, State.Position * 1000);
        }
        catch
        {
            if (seq != _loadSeq) return;
            State.Error ??= new PlayerError(PlayerErrorKind.SourceUnavailable);
            State.IsPlaying = false;
        }
    }

    private void HandleEngineCompletion()
    {
        if (_lastCompletedTrackId == State.Track?.Id) return;
        _lastCompletedTrackId = State.Track?.Id;
        if (State.Track is { } done)
            EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackCompleted, done));

        if (Queue.RepeatMode == RepeatMode.Off
            && Queue.CurrentIndex >= Queue.Items.Count - 1
            && Queue.UpNext.Count == 0)
        {
            _playbackRequested = false;
            State.IsPlaying = false;
            return;
        }

        if (_engine.PlayPrepared())
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
        EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackStarted, track));
    }
}
