using Muses.Persistence;

namespace Muses.Infrastructure;

/// <summary>
/// Local cache / reset helpers. Does not touch Credential Manager by itself.
/// </summary>
public static class LocalDataMaintenance
{
    public static string AppDataRoot() => Path.GetDirectoryName(SqliteStore.DefaultPath())!;

    public static string LocalCacheRoot() => CacheRootOverride ?? Path.Combine(AppDataRoot(), "cache");

    private static string? CacheRootOverride => AbsoluteOverride("MUSES_CACHE_HOME");

    private static string? AbsoluteOverride(string name) =>
        Environment.GetEnvironmentVariable(name) is { } path && Path.IsPathFullyQualified(path) ? path : null;

    public static int ClearCache()
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return CacheDirectories().Distinct(comparer).Sum(DeleteTree);
    }

    private static IEnumerable<string> CacheDirectories()
    {
        if (CacheRootOverride is { } cache)
        {
            foreach (var child in new[] { "artwork", "home-feed", "yt-dlp", "deno", "webview2" })
                yield return Path.Combine(cache, child);
            yield break;
        }

        yield return LocalCacheRoot();
        if (AbsoluteOverride("MUSES_DATA_HOME") is not null)
            yield break;

        foreach (var databasePath in SqliteStore.DefaultPaths())
        {
            var root = Path.GetDirectoryName(databasePath)!;
            yield return Path.Combine(root, "artwork");
            yield return Path.Combine(root, "cache");
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localData, "Muses", "Cache");
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var legacyCache = AbsoluteOverride("XDG_CACHE_HOME") ?? Path.Combine(home, ".cache");
            yield return Path.Combine(legacyCache, "Muses");
        }
    }

    /// <summary>
    /// Deletes the SQLite store and caches. Caller must dispose the live store first
    /// and sign out of the credential locker separately.
    /// </summary>
    public static void ResetDataFiles(string? sqlitePath = null)
    {
        ClearCache();
        if (sqlitePath is not null)
        {
            DeleteDatabase(sqlitePath);
            return;
        }

        // Remove legacy copies first. If one is locked, retain the active library
        // so the next launch cannot resurrect that older copy.
        var paths = SqliteStore.DefaultPaths();
        foreach (var legacy in paths.Skip(1))
            if (!DeleteDatabase(legacy))
                return;
        DeleteDatabase(paths[0]);
    }

    private static int DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return 0;
            var count = Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }).Count();
            Directory.Delete(path, recursive: true);
            return count;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static bool DeleteDatabase(string path)
    {
        try
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            File.Delete(path + "-journal");
            return true;
        }
        catch (DirectoryNotFoundException) { return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
