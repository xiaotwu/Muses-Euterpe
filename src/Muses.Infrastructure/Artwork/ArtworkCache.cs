using System.Security.Cryptography;

namespace Muses.Infrastructure.Artwork;

public sealed class ArtworkCache
{
    private readonly string _directory;
    private readonly HttpClient _http;

    public ArtworkCache(string? directory = null, HttpClient? http = null)
    {
        _directory = directory ?? DefaultDirectory();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        Directory.CreateDirectory(_directory);
    }

    public static string DefaultDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = OperatingSystem.IsMacOS() ? "MusesEuterpe" : "Muses";
        return Path.Combine(root, folder, "artwork");
    }

    public string? GetPathForUrl(string url)
    {
        var hash = ComputeHash(url);
        var file = Path.Combine(_directory, $"{hash}.jpg");
        return File.Exists(file) ? file : null;
    }

    public async Task<byte[]?> LoadDataAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var hash = ComputeHash(url);
        var filePath = Path.Combine(_directory, $"{hash}.jpg");

        if (File.Exists(filePath))
        {
            try
            {
                return await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            }
            catch
            {
                // If reading cached file fails, fall back to download
            }
        }

        try
        {
            var data = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (data is not null && data.Length > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await File.WriteAllBytesAsync(filePath, data).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort cache write
                    }
                }, CancellationToken.None);
            }
            return data;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
