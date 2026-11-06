using Muses.Infrastructure.Artwork;
using Muses.Persistence;

namespace Muses.Infrastructure;

/// <summary>
/// Local cache / reset helpers. Does not touch Credential Manager by itself.
/// </summary>
public static class LocalDataMaintenance
{
    public static string AppDataRoot() => Path.GetDirectoryName(SqliteStore.DefaultPath())!;

    public static string LocalCacheRoot()
    {
        var cacheOverride = CacheRootOverride();
        if (cacheOverride is not null)
            return cacheOverride;

        if (OperatingSystem.IsLinux())
        {
            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var rootPath = Path.IsPathFullyQualified(cacheHome ?? "")
                ? cacheHome!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            return Path.Combine(rootPath, "Muses");
        }

        return LegacyLocalCacheRoot();
    }

    internal static string ArtworkCacheRoot() => CacheRootOverride() ??
        (OperatingSystem.IsLinux() ? LocalCacheRoot() : AppDataRoot());

    private static string? CacheRootOverride()
    {
        var cacheHome = Environment.GetEnvironmentVariable("MUSES_CACHE_HOME");
        return Path.IsPathFullyQualified(cacheHome ?? "") ? cacheHome : null;
    }

    private static string LegacyLocalCacheRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Muses", "Cache");
    }

    public static int ClearCache()
    {
        var n = 0;
        n += DeleteTree(ArtworkCache.DefaultDirectory());
        var cacheRoot = LocalCacheRoot();
        n += DeleteTree(Path.Combine(cacheRoot, "home-feed"));
        n += DeleteTree(Path.Combine(cacheRoot, "yt-dlp"));
        n += DeleteTree(Path.Combine(cacheRoot, "deno"));
        foreach (var databasePath in SqliteStore.DefaultPaths())
        {
            var dataRoot = Path.GetDirectoryName(databasePath)!;
            n += DeleteTree(Path.Combine(dataRoot, "cache"));
            if (OperatingSystem.IsLinux())
                n += DeleteTree(Path.Combine(dataRoot, "artwork"));
        }

        if (OperatingSystem.IsLinux() && CacheRootOverride() is null)
            n += DeleteTree(Path.Combine(LegacyLocalCacheRoot(), "home-feed"));

        return n;
    }

    /// <summary>
    /// Deletes the SQLite store and caches. Caller must dispose the live store first
    /// and sign out of the credential locker separately.
    /// </summary>
    public static void ResetDataFiles(string? sqlitePath = null)
    {
        ClearCache();
        // Clear legacy defaults too so a reset does not reopen the previous database.
        foreach (var db in sqlitePath is null ? SqliteStore.DefaultPaths() : [sqlitePath])
        {
            TryDeleteFile(db);
            TryDeleteFile(db + "-wal");
            TryDeleteFile(db + "-shm");
        }
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
