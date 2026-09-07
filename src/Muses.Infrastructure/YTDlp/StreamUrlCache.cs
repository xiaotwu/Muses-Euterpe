using System.Text.Json;

namespace Muses.Infrastructure.YTDlp;

public sealed class StreamUrlCache
{
    private readonly TimeSpan _ttl;
    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries = new();
    private bool _loaded;

    public StreamUrlCache(TimeSpan? ttl = null, string? persistencePath = null)
    {
        _ttl = ttl ?? TimeSpan.FromHours(6);
        _path = persistencePath;
    }

    public bool TryGet(string videoId, string quality, out Uri url)
    {
        EnsureLoaded();
        url = default!;
        if (!_entries.TryGetValue(Key(videoId, quality), out var entry)) return false;
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.Remove(Key(videoId, quality));
            return false;
        }
        url = new Uri(entry.Url);
        return true;
    }

    public void Set(string videoId, string quality, Uri url)
    {
        EnsureLoaded();
        _entries[Key(videoId, quality)] = new Entry(url.ToString(), DateTimeOffset.UtcNow + _ttl);
        Persist();
    }

    public void Invalidate(string videoId, string quality)
    {
        EnsureLoaded();
        _entries.Remove(Key(videoId, quality));
        Persist();
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
                if (entry.ExpiresAt > now) _entries[key] = entry;
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
    private sealed record FileShape(Dictionary<string, Entry> Entries);
}
