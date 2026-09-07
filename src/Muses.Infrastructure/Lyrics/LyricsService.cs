using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Lyrics;

namespace Muses.Infrastructure.Lyrics;

public sealed class LyricsService
{
    private readonly ILyricsProvider _provider;
    private readonly ITrackRepository? _trackRepo;

    private TrackSnapshot? _currentTrack;
    private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
    private int _currentLineIndex = -1;
    private bool _isLoading;
    private int _manualOffsetMs;
    private int _detectedOffsetMs;

    public event Action? Changed;

    public TrackSnapshot? CurrentTrack => _currentTrack;
    public IReadOnlyList<LyricLine> Lines => _lines;
    public int CurrentLineIndex => _currentLineIndex;
    public bool IsLoading => _isLoading;
    public int ManualOffsetMs => _manualOffsetMs;
    public bool HasSyncedLyrics => _lines.Any(l => l.TimeMs.HasValue);
    public bool HasLyrics => _lines.Count > 0;

    public LyricsService(ILyricsProvider? provider = null, ITrackRepository? trackRepo = null)
    {
        _provider = provider ?? new LrclibLyricsProvider();
        _trackRepo = trackRepo;
    }

    public void SetTrack(TrackSnapshot? track)
    {
        if (_currentTrack?.Id == track?.Id) return;

        _currentTrack = track;
        _currentLineIndex = -1;
        _manualOffsetMs = track?.LyricsOffsetMs ?? 0;

        if (track is null)
        {
            _lines = Array.Empty<LyricLine>();
            _isLoading = false;
            Changed?.Invoke();
            return;
        }

        // 1. Check local cached lyrics on track
        if (!string.IsNullOrWhiteSpace(track.Lyrics))
        {
            var (lines, detected) = LrcParser.Parse(track.Lyrics, _manualOffsetMs);
            _lines = lines;
            _detectedOffsetMs = detected;
            _isLoading = false;
            Changed?.Invoke();
            return;
        }

        // 2. Fetch from provider
        _lines = Array.Empty<LyricLine>();
        _isLoading = true;
        Changed?.Invoke();

        _ = Task.Run(async () =>
        {
            var result = await _provider.FetchLyricsAsync(track.Title, track.Artist, track.AlbumTitle).ConfigureAwait(false);
            if (_currentTrack?.Id != track.Id) return;

            if (result is not null)
            {
                var rawLrc = result.SyncedLyrics ?? result.PlainLyrics;
                var (lines, detected) = LrcParser.Parse(rawLrc, _manualOffsetMs);
                _lines = lines;
                _detectedOffsetMs = detected;

                // Cache lyrics to track repository if available
                if (_trackRepo is not null && !string.IsNullOrWhiteSpace(rawLrc))
                {
                    try
                    {
                        var entity = _trackRepo.Get(track.Id);
                        if (entity is not null)
                        {
                            entity.Lyrics = rawLrc;
                            entity.LyricsOffsetMs = _manualOffsetMs;
                            _trackRepo.Upsert(entity);
                        }
                    }
                    catch
                    {
                        // Ignore persistence failure
                    }
                }
            }

            _isLoading = false;
            Changed?.Invoke();
        });
    }

    public void UpdatePosition(double positionSeconds)
    {
        if (_lines.Count == 0) return;

        var posMs = positionSeconds * 1000.0;
        var newIndex = LrcParser.FindCurrentLineIndex(_lines, posMs);
        if (newIndex != _currentLineIndex)
        {
            _currentLineIndex = newIndex;
            Changed?.Invoke();
        }
    }

    public void SetManualOffset(int offsetMs)
    {
        _manualOffsetMs = offsetMs;
        if (_currentTrack is not null && !string.IsNullOrWhiteSpace(_currentTrack.Lyrics))
        {
            var (lines, _) = LrcParser.Parse(_currentTrack.Lyrics, _manualOffsetMs);
            _lines = lines;
        }

        if (_trackRepo is not null && _currentTrack is not null)
        {
            try
            {
                var entity = _trackRepo.Get(_currentTrack.Id);
                if (entity is not null)
                {
                    entity.LyricsOffsetMs = _manualOffsetMs;
                    _trackRepo.Upsert(entity);
                }
            }
            catch
            {
                // Ignore persistence failure
            }
        }

        Changed?.Invoke();
    }
}
