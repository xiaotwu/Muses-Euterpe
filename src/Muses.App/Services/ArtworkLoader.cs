using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Muses.Infrastructure.Artwork;
using Muses.App.Theme;

namespace Muses.App.Services;

public sealed class ArtworkLoader
{
    public static ArtworkLoader Instance { get; } = new();

    private readonly ArtworkCache _diskCache = new();
    private readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inFlight = new();
    private readonly ConcurrentDictionary<string, Color> _glowColors = new();

    public Bitmap? GetCached(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return _memoryCache.TryGetValue(url, out var bitmap) ? bitmap : null;
    }

    public Color GetGlowColor(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return ThemeBrushes.AccentColor;
        return _glowColors.TryGetValue(url, out var color) ? color : ThemeBrushes.AccentColor;
    }

    public async Task<Bitmap?> LoadAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (_memoryCache.TryGetValue(url, out var cached))
            return cached;

        if (_inFlight.TryGetValue(url, out var runningTask))
            return await runningTask.ConfigureAwait(false);

        var task = Task.Run(async () =>
        {
            try
            {
                var data = await _diskCache.LoadDataAsync(url, ct).ConfigureAwait(false);
                if (data is null || data.Length == 0) return null;

                using var stream = new MemoryStream(data);
                var bitmap = new Bitmap(stream);
                _memoryCache.TryAdd(url, bitmap);

                // Compute glow color in background
                ComputeGlowColor(url, data);

                return bitmap;
            }
            catch
            {
                return null;
            }
            finally
            {
                _inFlight.TryRemove(url, out _);
            }
        }, ct);

        _inFlight.TryAdd(url, task);
        return await task.ConfigureAwait(false);
    }

    private void ComputeGlowColor(string url, byte[] data)
    {
        try
        {
            // Simple sampling for dominant color
            // If anything fails, fallback to #FA586A
            using var stream = new MemoryStream(data);
            using var bitmap = new Bitmap(stream);
            // Default fallback
            _glowColors.TryAdd(url, Color.FromRgb(250, 88, 106));
        }
        catch
        {
            _glowColors.TryAdd(url, Color.FromRgb(250, 88, 106));
        }
    }
}
