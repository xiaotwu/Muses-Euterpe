using Microsoft.Data.Sqlite;
using Muses.Core.Library;

namespace Muses.Persistence;

public sealed record PolyhymniaImportResult(int TracksAdded, int TracksUpdated, int PlaylistsAdded, int PlaylistEntriesAdded);

/// <summary>Merges a standalone Mac library backup into an initialized Euterpe store.</summary>
public sealed class PolyhymniaLibraryImporter(string destinationPath)
{
    private readonly string _destinationPath = Path.GetFullPath(destinationPath);

    public Task<PolyhymniaImportResult> ImportAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Import(Path.GetFullPath(path), cancellationToken), cancellationToken);

    private PolyhymniaImportResult Import(string sourcePath, CancellationToken cancellationToken)
    {
        var backup = PolyhymniaLibraryBackup.Read(sourcePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Own this connection so the application's active store stays on its usual thread.
            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _destinationPath,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                DefaultTimeout = 5,
                Pooling = false
            }.ToString());
            destination.Open();
            using var transaction = destination.BeginTransaction(deferred: false);
            var result = Merge(transaction, backup, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return result;
        }
        catch (SqliteException ex)
        {
            throw new IOException("Could not merge the Mac library backup into the desktop library.", ex);
        }
    }

    private static PolyhymniaImportResult Merge(SqliteTransaction transaction, PolyhymniaLibraryBackup backup,
        CancellationToken cancellationToken)
    {
        // Receipts outlive local edits and deletions. The import and its receipts commit together.
        Execute(transaction, """
            CREATE TABLE IF NOT EXISTS polyhymnia_import_receipts (
                kind TEXT NOT NULL,
                source_id TEXT NOT NULL,
                PRIMARY KEY (kind, source_id)
            );
            """);

        var tracksAdded = new HashSet<string>();
        var tracksUpdated = new HashSet<string>();
        var importedTracks = new Dictionary<long, (string VideoId, string? DestinationId)>();
        foreach (var track in backup.Tracks.OrderByDescending(t => t.Entity.PlayCount)
                     .ThenByDescending(t => t.Entity.LastPlayedAt).ThenBy(t => t.Entity.AddedAt).ThenBy(t => t.Entity.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = track.Entity;
            var destinationId = FindTrack(transaction, entity.YouTubeId);
            if (RecordImport(transaction, "track", entity.Id))
            {
                if (destinationId is null)
                {
                    destinationId = InsertTrack(transaction, entity);
                    tracksAdded.Add(destinationId);
                }
                else if (Execute(transaction, """
                    UPDATE tracks SET liked = MAX(liked, $liked), library_member = MAX(library_member, $member)
                    WHERE id = $id AND (liked < $liked OR library_member < $member);
                    """, ("$id", destinationId), ("$liked", entity.Liked), ("$member", entity.LibraryMember)) != 0)
                {
                    tracksUpdated.Add(destinationId);
                }
            }
            importedTracks.Add(track.Key, (entity.YouTubeId, destinationId));
        }

        var playlistsAdded = 0;
        var entriesAdded = 0;
        foreach (var playlist in backup.Playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RecordImport(transaction, playlist.YouTube is null ? "playlist" : "linked-playlist", playlist.Id)) continue;
            var playlistId = InsertPlaylist(transaction, playlist);
            if (playlistId is null) continue;
            playlistsAdded++;

            var order = 0;
            foreach (var entry in playlist.Entries.OrderBy(e => e.Order).ThenBy(e => e.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relatedTrack = entry.TrackKey is long key ? importedTracks.GetValueOrDefault(key) : default;
                var trackId = relatedTrack.DestinationId;
                if (entry.YouTube?.UnlinkedTrack is { } linked && relatedTrack.VideoId != linked.YouTubeId)
                {
                    trackId = FindTrack(transaction, linked.YouTubeId);
                    if (trackId is null)
                    {
                        trackId = InsertTrack(transaction, linked);
                        tracksAdded.Add(trackId);
                    }
                }

                InsertEntry(transaction, playlistId, trackId, entry, order++);
                entriesAdded++;
            }
        }

        tracksUpdated.ExceptWith(tracksAdded);
        return new PolyhymniaImportResult(tracksAdded.Count, tracksUpdated.Count, playlistsAdded, entriesAdded);
    }

    private static bool RecordImport(SqliteTransaction transaction, string kind, Guid sourceId) =>
        Execute(transaction, """
            INSERT INTO polyhymnia_import_receipts (kind, source_id) VALUES ($kind, $sourceId)
            ON CONFLICT(kind, source_id) DO NOTHING;
            """, ("$kind", kind), ("$sourceId", sourceId.ToString())) != 0;

    private static string? FindTrack(SqliteTransaction transaction, string videoId)
    {
        using var command = Command(transaction, "SELECT id FROM tracks WHERE youtube_id = $video LIMIT 1;", ("$video", videoId));
        return command.ExecuteScalar() as string;
    }

    private static string? InsertPlaylist(SqliteTransaction transaction, PolyhymniaLibraryBackup.Playlist playlist)
    {
        var createdAt = playlist.CreatedAt.ToUnixTimeMilliseconds();
        if (playlist.YouTube is not { } linked)
        {
            return InsertWithId(transaction, playlist.Id, """
                INSERT INTO playlists (id, name, created_at, pinned) VALUES ($id, $name, $created, $pinned)
                ON CONFLICT(id) DO NOTHING;
                """, ("$name", playlist.Name), ("$created", createdAt), ("$pinned", playlist.Pinned));
        }

        using var find = Command(transaction, "SELECT 1 FROM youtube_imports WHERE playlist_id = $playlist LIMIT 1;",
            ("$playlist", linked.Id));
        if (find.ExecuteScalar() is not null) return null;

        var url = $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(linked.Id)}";
        return InsertWithId(transaction, playlist.Id, """
            INSERT INTO youtube_imports (id, playlist_id, url, title, channel, artwork_url, imported_at)
            VALUES ($id, $playlist, $url, $title, $channel, $artwork, $created)
            ON CONFLICT(id) DO NOTHING;
            """, ("$playlist", linked.Id), ("$url", url), ("$title", playlist.Name),
            ("$channel", linked.Channel), ("$artwork", linked.ArtworkUrl), ("$created", createdAt));
    }

    private static void InsertEntry(SqliteTransaction transaction, string playlistId, string? trackId,
        PolyhymniaLibraryBackup.Entry entry, int order)
    {
        if (entry.YouTube is { } linked)
        {
            InsertWithId(transaction, entry.Id, """
                INSERT INTO youtube_import_items (id, import_id, track_id, youtube_id, title, item_order, playlist_item_id)
                VALUES ($id, $playlist, $track, $video, $title, $order, $resource)
                ON CONFLICT(id) DO NOTHING;
                """, ("$playlist", playlistId), ("$track", trackId), ("$video", linked.VideoId), ("$title", linked.Title),
                ("$order", order), ("$resource", linked.ResourceId));
        }
        else
        {
            InsertWithId(transaction, entry.Id, """
                INSERT INTO playlist_items (id, playlist_id, track_id, item_order) VALUES ($id, $playlist, $track, $order)
                ON CONFLICT(id) DO NOTHING;
                """, ("$playlist", playlistId), ("$track", trackId), ("$order", order));
        }
    }

    private static string InsertTrack(SqliteTransaction transaction, TrackEntity track)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        SqliteStore.BindImportedTrackInsert(command, track);
        return ExecuteInsert(command);
    }

    private static string InsertWithId(SqliteTransaction transaction, Guid preferredId, string sql,
        params (string Name, object? Value)[] values)
    {
        using var command = Command(transaction, sql, values);
        command.Parameters.AddWithValue("$id", preferredId.ToString());
        return ExecuteInsert(command);
    }

    private static string ExecuteInsert(SqliteCommand command)
    {
        var id = command.Parameters["$id"];
        if (command.ExecuteNonQuery() == 0)
        {
            id.Value = Guid.NewGuid().ToString();
            if (command.ExecuteNonQuery() == 0)
                throw new IOException("Could not assign a local ID to a Mac library record.");
        }
        return (string)id.Value!;
    }

    private static int Execute(SqliteTransaction transaction, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(transaction, sql, values);
        return command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteTransaction transaction, string sql, params (string Name, object? Value)[] values)
    {
        var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
