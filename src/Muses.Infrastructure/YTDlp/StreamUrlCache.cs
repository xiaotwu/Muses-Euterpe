using System.Text.Json;

namespace Muses.Infrastructure.YTDlp;

public sealed class StreamUrlCache
{
    private readonly TimeSpan _ttl;
    private readonly string? _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    // Request contexts and their signed URLs live only in memory.
    private readonly Dictionary<(object Context, string VideoId, string Quality), AudioEntry> _audioEntries = new();
    private bool _loaded;

    public StreamUrlCache(TimeSpan? ttl = null, string? persistencePath = null)
    {
        _ttl = ttl ?? TimeSpan.FromHours(6);
        _path = persistencePath;
    }

    public bool TryGet(string videoId, string quality, out Uri url)
    {
        lock (_gate)
        {
            EnsureLoaded();
            url = default!;
            var key = Key(videoId, quality);
            if (!_entries.TryGetValue(key, out var entry)) return false;
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow || !Uri.TryCreate(entry.Url, UriKind.Absolute, out var candidate))
            {
                _entries.Remove(key);
                return false;
            }
            url = candidate;
            return true;
        }
    }

    public void Set(string videoId, string quality, Uri url)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _entries[Key(videoId, quality)] = new Entry(url.ToString(),
                CacheExpiry(DateTimeOffset.UtcNow, AudioStreamJsonParser.ReadExpiry(url)));
            Persist();
        }
    }

    public void Invalidate(string videoId, string quality)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _entries.Remove(Key(videoId, quality));
            Persist();
        }
    }

    internal ResolvedAudioStream? GetAudio(object context, string videoId, string quality)
    {
        lock (_gate)
        {
            var key = (context, videoId, quality);
            if (!_audioEntries.TryGetValue(key, out var entry)) return null;
            if (entry.ExpiresAt > DateTimeOffset.UtcNow) return entry.Stream;
            _audioEntries.Remove(key);
            return null;
        }
    }

    internal void SetAudio(object context, string videoId, string quality, ResolvedAudioStream stream)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = CacheExpiry(now, stream.ExpiresAt);
        if (expiry <= now) return;

        lock (_gate)
        {
            var key = (context, videoId, quality);
            foreach (var expired in _audioEntries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToArray())
                _audioEntries.Remove(expired);
            if (_audioEntries.Count >= 128 && !_audioEntries.ContainsKey(key))
                _audioEntries.Remove(_audioEntries.MinBy(e => e.Value.ExpiresAt).Key);
            _audioEntries[key] = new AudioEntry(stream, expiry);
        }
    }

    internal void InvalidateAudio(object context, string videoId, string quality, ResolvedAudioStream stream)
    {
        lock (_gate)
        {
            var key = (context, videoId, quality);
            if (_audioEntries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Stream, stream))
                _audioEntries.Remove(key);
        }
    }

    private DateTimeOffset CacheExpiry(DateTimeOffset now, DateTimeOffset? signedExpiry)
    {
        if (signedExpiry <= now + ResolvedAudioStream.ExpiryMargin) return now;
        var requestedExpiry = now + _ttl;
        return signedExpiry - ResolvedAudioStream.ExpiryMargin is { } safeExpiry && safeExpiry < requestedExpiry
            ? safeExpiry
            : requestedExpiry;
    }

    private static string Key(string videoId, string quality) => $"{videoId}__{quality}";

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<FileShape>(json);
            if (file?.Entries is null) return;
            var now = DateTimeOffset.UtcNow;
            foreach (var (key, entry) in file.Entries)
            {
                if (entry.ExpiresAt <= now || !Uri.TryCreate(entry.Url, UriKind.Absolute, out var url)) continue;
                var expiry = CacheExpiry(now, AudioStreamJsonParser.ReadExpiry(url));
                if (expiry > now)
                    _entries[key] = entry.ExpiresAt <= expiry ? entry : entry with { ExpiresAt = expiry };
            }
        }
        catch
        {
            // Corrupt cache is ignored.
        }
    }

    private void Persist()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(new FileShape(_entries));
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Disk persistence is best-effort.
        }
    }

    private sealed record Entry(string Url, DateTimeOffset ExpiresAt);
    private sealed record AudioEntry(ResolvedAudioStream Stream, DateTimeOffset ExpiresAt);
    private sealed record FileShape(Dictionary<string, Entry> Entries);
}
