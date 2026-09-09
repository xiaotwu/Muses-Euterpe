using Muses.Infrastructure.Artwork;

namespace Muses.Infrastructure;

/// <summary>
/// Local cache / reset helpers. Does not touch Credential Manager by itself.
/// </summary>
public static class LocalDataMaintenance
{
    public static string AppDataRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = OperatingSystem.IsMacOS() ? "MusesEuterpe" : "Muses";
        return Path.Combine(root, folder);
    }

    public static string LocalCacheRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Muses", "Cache");
    }

    public static int ClearCache()
    {
        var n = 0;
        n += DeleteTree(ArtworkCache.DefaultDirectory());
        n += DeleteTree(Path.Combine(LocalCacheRoot(), "home-feed"));
        n += DeleteTree(Path.Combine(AppDataRoot(), "cache"));
        return n;
    }

    /// <summary>
    /// Deletes the SQLite store and caches. Caller must dispose the live store first
    /// and sign out of the credential locker separately.
    /// </summary>
    public static void ResetDataFiles(string? sqlitePath = null)
    {
        ClearCache();
        var db = sqlitePath ?? Path.Combine(AppDataRoot(), "muses-youtube-native.sqlite");
        TryDeleteFile(db);
        TryDeleteFile(db + "-wal");
        TryDeleteFile(db + "-shm");
    }

    private static int DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return 0;
        var count = 0;
        try
        {
            count = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
        return count;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort; file may still be locked.
        }
    }
}
