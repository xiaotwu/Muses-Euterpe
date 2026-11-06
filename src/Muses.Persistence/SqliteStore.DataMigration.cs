using Microsoft.Data.Sqlite;

namespace Muses.Persistence;

public sealed partial class SqliteStore
{
    internal static string SelectDatabasePath(IReadOnlyList<string> paths)
    {
        var preferred = paths[0];
        if (File.Exists(preferred))
            return preferred;

        var legacy = paths.Skip(1).FirstOrDefault(File.Exists);
        if (legacy is null)
            return preferred;

        var directory = Path.GetDirectoryName(preferred)!;
        var staging = Path.Combine(directory, $".muses-library-{Guid.NewGuid():N}.sqlite");
        try
        {
            Directory.CreateDirectory(directory);
            using (var source = OpenMigrationConnection(legacy, SqliteOpenMode.ReadOnly))
            using (var target = OpenMigrationConnection(staging, SqliteOpenMode.ReadWriteCreate))
            {
                // Copy the SQLite snapshot, including committed WAL frames.
                source.BackupDatabase(target);
                MusesSchema.Ensure(target);
                using var command = target.CreateCommand();
                command.CommandText = "PRAGMA quick_check(1);";
                if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("The migrated library failed SQLite's integrity check.");

                // Publish a standalone file with no dependency on staging sidecars.
                command.CommandText = "PRAGMA journal_mode = DELETE;";
                if (!string.Equals(command.ExecuteScalar() as string, "delete", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Could not finalize the migrated library.");
            }

            File.Move(staging, preferred, overwrite: false);
            return preferred;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // A concurrent launch may already have published its own copy.
            // If migration is unavailable, keep using the original library.
            return File.Exists(preferred) ? preferred : legacy;
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(staging + suffix); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static SqliteConnection OpenMigrationConnection(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
