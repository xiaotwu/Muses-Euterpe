using Microsoft.Data.Sqlite;
using Muses.Core.Advanced;
using Muses.Core.Catalog;
using Muses.Core.Domain;
using Muses.Core.History;
using Muses.Core.Library;
using Muses.Core.Queue;

namespace Muses.Persistence;

public sealed class SqliteStore : IDisposable, IQueueStore, ITrackRepository, IPlaylistRepository, IYouTubeImportRepository, ICatalogRepository, IHistoryRepository, IEQRepository, INotesRepository, IInboxRepository, IAutomationRepository, IFocusRepository
{
    private readonly SqliteConnection _connection;
    public bool UsedInMemoryFallback { get; }
    public string? FailureDescription { get; }

    private SqliteStore(SqliteConnection connection, bool usedInMemoryFallback, string? failureDescription)
    {
        _connection = connection;
        UsedInMemoryFallback = usedInMemoryFallback;
        FailureDescription = failureDescription;
        MusesSchema.Ensure(_connection);
    }

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = OperatingSystem.IsMacOS() ? "MusesEuterpe" : "Muses";
        return Path.Combine(root, folder, "muses-youtube-native.sqlite");
    }

    public static SqliteStore Open(string? path = null, bool inMemory = false)
    {
        if (inMemory)
            return new SqliteStore(OpenMemory(), false, null);

        var destination = path ?? DefaultPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = destination,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            conn.Open();
            return new SqliteStore(conn, false, null);
        }
        catch (Exception ex)
        {
            BackupCorrupt(destination);
            return new SqliteStore(OpenMemory(), true, ex.Message);
        }
    }

    public void Save(QueueStateRecord state)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO queue_state (id, items_json, current_index, up_next_json, history_json,
                repeat_mode_raw, shuffle, saved_at, current_track_id, last_position_ms, groups_json)
            VALUES ($id, $items, $idx, $up, $hist, $repeat, $shuffle, $saved, $track, $pos, $groups)
            ON CONFLICT(id) DO UPDATE SET
                items_json = excluded.items_json,
                current_index = excluded.current_index,
                up_next_json = excluded.up_next_json,
                history_json = excluded.history_json,
                repeat_mode_raw = excluded.repeat_mode_raw,
                shuffle = excluded.shuffle,
                saved_at = excluded.saved_at,
                current_track_id = excluded.current_track_id,
                last_position_ms = excluded.last_position_ms,
                groups_json = excluded.groups_json;
            """;
        cmd.Parameters.AddWithValue("$id", state.Id.ToString());
        cmd.Parameters.AddWithValue("$items", state.ItemsJson);
        cmd.Parameters.AddWithValue("$idx", state.CurrentIndex);
        cmd.Parameters.AddWithValue("$up", state.UpNextJson);
        cmd.Parameters.AddWithValue("$hist", state.HistoryJson);
        cmd.Parameters.AddWithValue("$repeat", state.RepeatModeRaw);
        cmd.Parameters.AddWithValue("$shuffle", state.Shuffle ? 1 : 0);
        cmd.Parameters.AddWithValue("$saved", state.SavedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$track", (object?)state.CurrentTrackId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pos", (object?)state.LastPositionMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$groups", (object?)state.GroupsJson ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public QueueStateRecord? Load()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM queue_state WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", QueueStateRecord.SharedId.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new QueueStateRecord
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ItemsJson = reader.GetString(reader.GetOrdinal("items_json")),
            CurrentIndex = reader.GetInt32(reader.GetOrdinal("current_index")),
            UpNextJson = reader.GetString(reader.GetOrdinal("up_next_json")),
            HistoryJson = reader.GetString(reader.GetOrdinal("history_json")),
            RepeatModeRaw = reader.GetString(reader.GetOrdinal("repeat_mode_raw")),
            Shuffle = reader.GetInt32(reader.GetOrdinal("shuffle")) != 0,
            SavedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("saved_at"))),
            CurrentTrackId = GetGuid(reader, "current_track_id"),
            LastPositionMs = GetDouble(reader, "last_position_ms"),
            GroupsJson = GetString(reader, "groups_json")
        };
    }

    public void Upsert(TrackEntity track)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tracks (id, title, artist, album_title, album_artist, duration_ms, track_no, disc_no, year, genre,
                youtube_id, media_kind_raw, release_catalog_id, release_order, artist_catalog_id, artwork_url, lyrics,
                lyrics_offset_ms, replay_gain, sample_rate, bit_depth, codec, bit_rate, channels, is_lossless,
                metadata_status_raw, availability_raw, added_at, last_played_at, play_count, liked)
            VALUES ($id, $title, $artist, $album, $albumArtist, $dur, $trackNo, $discNo, $year, $genre,
                $yt, $kind, $rel, $relOrd, $artCat, $art, $lyrics, $lyOff, $rg, $sr, $bd, $codec, $br, $ch, $lossless,
                $meta, $avail, $added, $last, $plays, $liked)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title, artist = excluded.artist, album_title = excluded.album_title,
                album_artist = excluded.album_artist, duration_ms = excluded.duration_ms,
                year = excluded.year, youtube_id = excluded.youtube_id,
                release_catalog_id = excluded.release_catalog_id, release_order = excluded.release_order,
                artist_catalog_id = excluded.artist_catalog_id,
                artwork_url = excluded.artwork_url, lyrics = excluded.lyrics,
                last_played_at = excluded.last_played_at, play_count = excluded.play_count,
                liked = excluded.liked;
            """;
        BindTrack(cmd, track);
        cmd.ExecuteNonQuery();
    }

    public TrackEntity? Get(Guid id) => QueryTrack("SELECT * FROM tracks WHERE id = $id LIMIT 1;",
        cmd => cmd.Parameters.AddWithValue("$id", id.ToString()));

    public TrackEntity? GetByYouTubeId(string youTubeId) => QueryTrack(
        "SELECT * FROM tracks WHERE youtube_id = $yt LIMIT 1;",
        cmd => cmd.Parameters.AddWithValue("$yt", youTubeId));

    public IReadOnlyList<TrackEntity> All()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks;";
        using var reader = cmd.ExecuteReader();
        var list = new List<TrackEntity>();
        while (reader.Read()) list.Add(ReadTrack(reader));
        return list;
    }

    public void RecordPlay(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks SET play_count = play_count + 1, last_played_at = $now WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    public void ToggleLike(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET liked = CASE liked WHEN 1 THEN 0 ELSE 1 END WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    // --- IPlaylistRepository ---

    public PlaylistEntity Create(string name)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlists (id, name, created_at, pinned)
            VALUES ($id, $name, $created, 0);
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$created", now.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
        return new PlaylistEntity
        {
            Id = id,
            Name = name,
            CreatedAt = now,
            Pinned = false,
            Items = []
        };
    }

    public void Rename(Guid id, string newName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", newName);
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM playlist_items WHERE playlist_id = $id;
            DELETE FROM playlists WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public PlaylistDeletionSnapshot? DeleteWithUndoSnapshot(Guid id)
    {
        var playlist = GetPlaylist(id);
        if (playlist is null) return null;

        var snapshot = new PlaylistDeletionSnapshot(
            playlist.Id,
            playlist.Name,
            playlist.CreatedAt,
            playlist.Pinned,
            playlist.Items.Select(i => new PlaylistDeletionItemSnapshot(i.ItemOrder, i.TrackId)).ToList());

        Delete(id);
        return snapshot;
    }

    public PlaylistEntity? Restore(PlaylistDeletionSnapshot snapshot)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlists (id, name, created_at, pinned)
            VALUES ($id, $name, $created, $pinned)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, pinned = excluded.pinned;
            """;
        cmd.Parameters.AddWithValue("$id", snapshot.Id.ToString());
        cmd.Parameters.AddWithValue("$name", snapshot.Name);
        cmd.Parameters.AddWithValue("$created", snapshot.CreatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$pinned", snapshot.Pinned ? 1 : 0);
        cmd.ExecuteNonQuery();

        var items = new List<PlaylistItemEntity>();
        foreach (var item in snapshot.Items)
        {
            var itemId = Guid.NewGuid();
            using var itemCmd = _connection.CreateCommand();
            itemCmd.CommandText = """
                INSERT INTO playlist_items (id, playlist_id, track_id, item_order)
                VALUES ($id, $pid, $tid, $ord);
                """;
            itemCmd.Parameters.AddWithValue("$id", itemId.ToString());
            itemCmd.Parameters.AddWithValue("$pid", snapshot.Id.ToString());
            itemCmd.Parameters.AddWithValue("$tid", (object?)item.TrackId?.ToString() ?? DBNull.Value);
            itemCmd.Parameters.AddWithValue("$ord", item.Order);
            itemCmd.ExecuteNonQuery();

            TrackEntity? track = item.TrackId.HasValue ? Get(item.TrackId.Value) : null;
            items.Add(new PlaylistItemEntity
            {
                Id = itemId,
                PlaylistId = snapshot.Id,
                TrackId = item.TrackId,
                ItemOrder = item.Order,
                Track = track
            });
        }

        return new PlaylistEntity
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            CreatedAt = snapshot.CreatedAt,
            Pinned = snapshot.Pinned,
            Items = items
        };
    }

    public IReadOnlyList<PlaylistEntity> GetAll()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM playlists ORDER BY created_at DESC;";
        using var reader = cmd.ExecuteReader();
        var playlists = new List<PlaylistEntity>();
        while (reader.Read())
        {
            playlists.Add(new PlaylistEntity
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Name = reader.GetString(reader.GetOrdinal("name")),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("created_at"))),
                Pinned = reader.GetInt32(reader.GetOrdinal("pinned")) != 0
            });
        }
        foreach (var p in playlists)
        {
            p.Items = LoadPlaylistItems(p.Id);
        }
        return playlists;
    }

    public PlaylistEntity? GetPlaylist(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM playlists WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var playlist = new PlaylistEntity
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Name = reader.GetString(reader.GetOrdinal("name")),
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("created_at"))),
            Pinned = reader.GetInt32(reader.GetOrdinal("pinned")) != 0
        };
        playlist.Items = LoadPlaylistItems(playlist.Id);
        return playlist;
    }

    PlaylistEntity? IPlaylistRepository.Get(Guid id) => GetPlaylist(id);

    public void AddTrack(Guid playlistId, Guid trackId)
    {
        using (var checkCmd = _connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT 1 FROM playlist_items WHERE playlist_id = $pid AND track_id = $tid LIMIT 1;";
            checkCmd.Parameters.AddWithValue("$pid", playlistId.ToString());
            checkCmd.Parameters.AddWithValue("$tid", trackId.ToString());
            if (checkCmd.ExecuteScalar() != null) return; // Deduplicated!
        }

        int nextOrder = 0;
        using (var orderCmd = _connection.CreateCommand())
        {
            orderCmd.CommandText = "SELECT COALESCE(MAX(item_order) + 1, 0) FROM playlist_items WHERE playlist_id = $pid;";
            orderCmd.Parameters.AddWithValue("$pid", playlistId.ToString());
            var res = orderCmd.ExecuteScalar();
            if (res != null && res != DBNull.Value) nextOrder = Convert.ToInt32(res);
        }

        using var insCmd = _connection.CreateCommand();
        insCmd.CommandText = """
            INSERT INTO playlist_items (id, playlist_id, track_id, item_order)
            VALUES ($id, $pid, $tid, $ord);
            """;
        insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insCmd.Parameters.AddWithValue("$pid", playlistId.ToString());
        insCmd.Parameters.AddWithValue("$tid", trackId.ToString());
        insCmd.Parameters.AddWithValue("$ord", nextOrder);
        insCmd.ExecuteNonQuery();
    }

    public void RemoveItem(Guid itemId)
    {
        Guid? playlistId = null;
        using (var getCmd = _connection.CreateCommand())
        {
            getCmd.CommandText = "SELECT playlist_id FROM playlist_items WHERE id = $id LIMIT 1;";
            getCmd.Parameters.AddWithValue("$id", itemId.ToString());
            var raw = getCmd.ExecuteScalar() as string;
            if (raw != null) playlistId = Guid.Parse(raw);
        }
        if (playlistId is null) return;

        using (var delCmd = _connection.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM playlist_items WHERE id = $id;";
            delCmd.Parameters.AddWithValue("$id", itemId.ToString());
            delCmd.ExecuteNonQuery();
        }

        RenumberPlaylistItems(playlistId.Value);
    }

    public void MoveItem(Guid playlistId, int fromIndex, int toIndex)
    {
        var items = LoadPlaylistItems(playlistId);
        if (fromIndex < 0 || fromIndex >= items.Count || toIndex < 0 || toIndex > items.Count) return;

        var target = items[fromIndex];
        items.RemoveAt(fromIndex);
        items.Insert(Math.Min(toIndex, items.Count), target);

        using var tx = _connection.BeginTransaction();
        for (var i = 0; i < items.Count; i++)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE playlist_items SET item_order = $ord WHERE id = $id;";
            cmd.Parameters.AddWithValue("$ord", i);
            cmd.Parameters.AddWithValue("$id", items[i].Id.ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void TogglePin(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET pinned = CASE pinned WHEN 1 THEN 0 ELSE 1 END WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PlaylistEntity> GetPinned()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM playlists WHERE pinned = 1 ORDER BY name ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<PlaylistEntity>();
        while (reader.Read())
        {
            list.Add(new PlaylistEntity
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Name = reader.GetString(reader.GetOrdinal("name")),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("created_at"))),
                Pinned = true
            });
        }
        foreach (var p in list)
        {
            p.Items = LoadPlaylistItems(p.Id);
        }
        return list;
    }

    private List<PlaylistItemEntity> LoadPlaylistItems(Guid playlistId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM playlist_items WHERE playlist_id = $pid ORDER BY item_order ASC, id ASC;";
        cmd.Parameters.AddWithValue("$pid", playlistId.ToString());
        using var reader = cmd.ExecuteReader();
        var items = new List<PlaylistItemEntity>();
        while (reader.Read())
        {
            items.Add(new PlaylistItemEntity
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                PlaylistId = playlistId,
                TrackId = GetGuid(reader, "track_id"),
                ItemOrder = reader.GetInt32(reader.GetOrdinal("item_order"))
            });
        }
        foreach (var item in items)
        {
            if (item.TrackId.HasValue) item.Track = Get(item.TrackId.Value);
        }
        return items;
    }

    private void RenumberPlaylistItems(Guid playlistId)
    {
        var items = LoadPlaylistItems(playlistId);
        using var tx = _connection.BeginTransaction();
        for (var i = 0; i < items.Count; i++)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE playlist_items SET item_order = $ord WHERE id = $id;";
            cmd.Parameters.AddWithValue("$ord", i);
            cmd.Parameters.AddWithValue("$id", items[i].Id.ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // --- IYouTubeImportRepository ---

    public void UpsertImport(YouTubeImportEntity imp)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO youtube_imports (id, playlist_id, url, title, channel, artwork_url, imported_at, last_synced_at, account_channel_id, deleted_at)
            VALUES ($id, $pid, $url, $title, $channel, $art, $imported, $synced, $account, $deleted)
            ON CONFLICT(id) DO UPDATE SET
                playlist_id = excluded.playlist_id,
                url = excluded.url,
                title = excluded.title,
                channel = excluded.channel,
                artwork_url = excluded.artwork_url,
                last_synced_at = excluded.last_synced_at,
                account_channel_id = excluded.account_channel_id,
                deleted_at = excluded.deleted_at;
            """;
        cmd.Parameters.AddWithValue("$id", imp.Id.ToString());
        cmd.Parameters.AddWithValue("$pid", imp.PlaylistId);
        cmd.Parameters.AddWithValue("$url", imp.Url);
        cmd.Parameters.AddWithValue("$title", imp.Title);
        cmd.Parameters.AddWithValue("$channel", imp.Channel);
        cmd.Parameters.AddWithValue("$art", (object?)imp.ArtworkUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$imported", imp.ImportedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$synced", (object?)imp.LastSyncedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$account", (object?)imp.AccountChannelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deleted", (object?)imp.DeletedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var delItems = _connection.CreateCommand();
        delItems.CommandText = "DELETE FROM youtube_import_items WHERE import_id = $id;";
        delItems.Parameters.AddWithValue("$id", imp.Id.ToString());
        delItems.ExecuteNonQuery();

        using var tx = _connection.BeginTransaction();
        for (var i = 0; i < imp.Items.Count; i++)
        {
            var item = imp.Items[i];
            using var itemCmd = _connection.CreateCommand();
            itemCmd.Transaction = tx;
            itemCmd.CommandText = """
                INSERT INTO youtube_import_items (id, import_id, track_id, youtube_id, title, item_order, playlist_item_id)
                VALUES ($id, $impId, $tid, $yt, $title, $ord, $pItemId);
                """;
            itemCmd.Parameters.AddWithValue("$id", item.Id.ToString());
            itemCmd.Parameters.AddWithValue("$impId", imp.Id.ToString());
            itemCmd.Parameters.AddWithValue("$tid", (object?)item.TrackId?.ToString() ?? DBNull.Value);
            itemCmd.Parameters.AddWithValue("$yt", item.YouTubeId);
            itemCmd.Parameters.AddWithValue("$title", item.Title);
            itemCmd.Parameters.AddWithValue("$ord", item.ItemOrder);
            itemCmd.Parameters.AddWithValue("$pItemId", (object?)item.PlaylistItemId ?? DBNull.Value);
            itemCmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public YouTubeImportEntity? GetImport(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM youtube_imports WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var imp = ReadImport(reader);
        imp.Items = LoadImportItems(imp.Id);
        return imp;
    }

    public YouTubeImportEntity? GetImportByPlaylistId(string playlistId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM youtube_imports WHERE playlist_id = $pid LIMIT 1;";
        cmd.Parameters.AddWithValue("$pid", playlistId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var imp = ReadImport(reader);
        imp.Items = LoadImportItems(imp.Id);
        return imp;
    }

    public IReadOnlyList<YouTubeImportEntity> GetAllImports(bool includeDeleted = false)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT * FROM youtube_imports ORDER BY imported_at DESC;"
            : "SELECT * FROM youtube_imports WHERE deleted_at IS NULL ORDER BY imported_at DESC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<YouTubeImportEntity>();
        while (reader.Read())
        {
            list.Add(ReadImport(reader));
        }
        foreach (var imp in list)
        {
            imp.Items = LoadImportItems(imp.Id);
        }
        return list;
    }

    public void MarkDeleted(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE youtube_imports SET deleted_at = $now WHERE id = $id;";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void Restore(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE youtube_imports SET deleted_at = NULL WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void HardDelete(Guid id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM youtube_import_items WHERE import_id = $id;
            DELETE FROM youtube_imports WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void RemoveImportItem(Guid importId, Guid itemId)
    {
        using (var delCmd = _connection.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM youtube_import_items WHERE id = $id AND import_id = $impId;";
            delCmd.Parameters.AddWithValue("$id", itemId.ToString());
            delCmd.Parameters.AddWithValue("$impId", importId.ToString());
            delCmd.ExecuteNonQuery();
        }
        RenumberImportItems(importId);
    }

    public void MoveImportItem(Guid importId, int fromIndex, int toIndex)
    {
        var items = LoadImportItems(importId);
        if (fromIndex < 0 || fromIndex >= items.Count || toIndex < 0 || toIndex > items.Count) return;

        var target = items[fromIndex];
        items.RemoveAt(fromIndex);
        items.Insert(Math.Min(toIndex, items.Count), target);

        using var tx = _connection.BeginTransaction();
        for (var i = 0; i < items.Count; i++)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE youtube_import_items SET item_order = $ord WHERE id = $id;";
            cmd.Parameters.AddWithValue("$ord", i);
            cmd.Parameters.AddWithValue("$id", items[i].Id.ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private List<YouTubeImportItemEntity> LoadImportItems(Guid importId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM youtube_import_items WHERE import_id = $id ORDER BY item_order ASC, id ASC;";
        cmd.Parameters.AddWithValue("$id", importId.ToString());
        using var reader = cmd.ExecuteReader();
        var items = new List<YouTubeImportItemEntity>();
        while (reader.Read())
        {
            items.Add(new YouTubeImportItemEntity
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                ImportId = importId,
                TrackId = GetGuid(reader, "track_id"),
                YouTubeId = reader.GetString(reader.GetOrdinal("youtube_id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                ItemOrder = reader.GetInt32(reader.GetOrdinal("item_order")),
                PlaylistItemId = GetString(reader, "playlist_item_id")
            });
        }
        foreach (var item in items)
        {
            if (item.TrackId.HasValue) item.Track = Get(item.TrackId.Value);
        }
        return items;
    }

    private void RenumberImportItems(Guid importId)
    {
        var items = LoadImportItems(importId);
        using var tx = _connection.BeginTransaction();
        for (var i = 0; i < items.Count; i++)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE youtube_import_items SET item_order = $ord WHERE id = $id;";
            cmd.Parameters.AddWithValue("$ord", i);
            cmd.Parameters.AddWithValue("$id", items[i].Id.ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static YouTubeImportEntity ReadImport(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        PlaylistId = reader.GetString(reader.GetOrdinal("playlist_id")),
        Url = reader.GetString(reader.GetOrdinal("url")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Channel = reader.GetString(reader.GetOrdinal("channel")),
        ArtworkUrl = GetString(reader, "artwork_url"),
        ImportedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("imported_at"))),
        LastSyncedAt = GetLong(reader, "last_synced_at") is { } s ? DateTimeOffset.FromUnixTimeMilliseconds(s) : null,
        AccountChannelId = GetString(reader, "account_channel_id"),
        DeletedAt = GetLong(reader, "deleted_at") is { } d ? DateTimeOffset.FromUnixTimeMilliseconds(d) : null
    };

    // --- ICatalogRepository ---

    public void UpsertRelease(CatalogReleaseEntity release)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_releases (id, catalog_id, title, artist_name, kind_raw, artwork_url, year, artist_catalog_id, refreshed_at, unavailable)
            VALUES ($id, $catId, $title, $artist, $kind, $art, $year, $artCatId, $refreshed, $unavail)
            ON CONFLICT(catalog_id) DO UPDATE SET
                title = excluded.title,
                artist_name = excluded.artist_name,
                kind_raw = excluded.kind_raw,
                artwork_url = excluded.artwork_url,
                year = excluded.year,
                artist_catalog_id = excluded.artist_catalog_id,
                refreshed_at = excluded.refreshed_at,
                unavailable = excluded.unavailable;
            """;
        cmd.Parameters.AddWithValue("$id", release.Id.ToString());
        cmd.Parameters.AddWithValue("$catId", release.CatalogId);
        cmd.Parameters.AddWithValue("$title", release.Title);
        cmd.Parameters.AddWithValue("$artist", (object?)release.ArtistName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", (object?)release.KindRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$art", (object?)release.ArtworkUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$year", (object?)release.Year ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$artCatId", (object?)release.ArtistCatalogId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$refreshed", release.RefreshedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$unavail", release.Unavailable ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void UpsertArtist(CatalogArtistEntity artist)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_artists (id, catalog_id, name, artwork_url, channel_id, browse_id, biography, refreshed_at, unavailable)
            VALUES ($id, $catId, $name, $art, $chan, $browse, $bio, $refreshed, $unavail)
            ON CONFLICT(catalog_id) DO UPDATE SET
                name = excluded.name,
                artwork_url = excluded.artwork_url,
                channel_id = excluded.channel_id,
                browse_id = excluded.browse_id,
                biography = excluded.biography,
                refreshed_at = excluded.refreshed_at,
                unavailable = excluded.unavailable;
            """;
        cmd.Parameters.AddWithValue("$id", artist.Id.ToString());
        cmd.Parameters.AddWithValue("$catId", artist.CatalogId);
        cmd.Parameters.AddWithValue("$name", artist.Name);
        cmd.Parameters.AddWithValue("$art", (object?)artist.ArtworkUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$chan", (object?)artist.ChannelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$browse", (object?)artist.BrowseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bio", (object?)artist.Biography ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$refreshed", artist.RefreshedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$unavail", artist.Unavailable ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<CatalogReleaseEntity> GetReleases()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM catalog_releases ORDER BY title COLLATE NOCASE ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<CatalogReleaseEntity>();
        while (reader.Read())
        {
            list.Add(ReadRelease(reader));
        }
        return list;
    }

    public IReadOnlyList<CatalogArtistEntity> GetArtists()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM catalog_artists ORDER BY name COLLATE NOCASE ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<CatalogArtistEntity>();
        while (reader.Read())
        {
            list.Add(ReadArtist(reader));
        }
        return list;
    }

    public CatalogReleaseEntity? GetRelease(string catalogId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM catalog_releases WHERE catalog_id = $catId LIMIT 1;";
        cmd.Parameters.AddWithValue("$catId", catalogId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadRelease(reader) : null;
    }

    public CatalogArtistEntity? GetArtist(string catalogId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM catalog_artists WHERE catalog_id = $catId LIMIT 1;";
        cmd.Parameters.AddWithValue("$catId", catalogId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadArtist(reader) : null;
    }

    public void DeleteOrphanReleases(ISet<string> activeCatalogIds)
    {
        var all = GetReleases();
        using var tx = _connection.BeginTransaction();
        foreach (var rel in all)
        {
            if (!activeCatalogIds.Contains(rel.CatalogId))
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM catalog_releases WHERE catalog_id = $catId;";
                cmd.Parameters.AddWithValue("$catId", rel.CatalogId);
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void DeleteOrphanArtists(ISet<string> activeCatalogIds)
    {
        var all = GetArtists();
        using var tx = _connection.BeginTransaction();
        foreach (var art in all)
        {
            if (!activeCatalogIds.Contains(art.CatalogId))
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM catalog_artists WHERE catalog_id = $catId;";
                cmd.Parameters.AddWithValue("$catId", art.CatalogId);
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private static CatalogReleaseEntity ReadRelease(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        CatalogId = reader.GetString(reader.GetOrdinal("catalog_id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        ArtistName = GetString(reader, "artist_name"),
        KindRaw = GetString(reader, "kind_raw"),
        ArtworkUrl = GetString(reader, "artwork_url"),
        Year = GetInt(reader, "year"),
        ArtistCatalogId = GetString(reader, "artist_catalog_id"),
        RefreshedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("refreshed_at"))),
        Unavailable = reader.GetInt32(reader.GetOrdinal("unavailable")) != 0
    };

    private static CatalogArtistEntity ReadArtist(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        CatalogId = reader.GetString(reader.GetOrdinal("catalog_id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        ArtworkUrl = GetString(reader, "artwork_url"),
        ChannelId = GetString(reader, "channel_id"),
        BrowseId = GetString(reader, "browse_id"),
        Biography = GetString(reader, "biography"),
        RefreshedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("refreshed_at"))),
        Unavailable = reader.GetInt32(reader.GetOrdinal("unavailable")) != 0
    };

    public void Dispose() => _connection.Dispose();

    private TrackEntity? QueryTrack(string sql, Action<SqliteCommand> bind)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadTrack(reader) : null;
    }

    private static TrackEntity ReadTrack(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Artist = reader.GetString(reader.GetOrdinal("artist")),
        AlbumTitle = GetString(reader, "album_title"),
        AlbumArtist = GetString(reader, "album_artist"),
        DurationMs = reader.GetInt32(reader.GetOrdinal("duration_ms")),
        YouTubeId = reader.GetString(reader.GetOrdinal("youtube_id")),
        ArtworkUrl = GetString(reader, "artwork_url"),
        Lyrics = GetString(reader, "lyrics"),
        PlayCount = reader.GetInt32(reader.GetOrdinal("play_count")),
        Liked = reader.GetInt32(reader.GetOrdinal("liked")) != 0,
        AddedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("added_at"))),
        LastPlayedAt = GetLong(reader, "last_played_at") is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null,
        TrackNo = GetInt(reader, "track_no"),
        DiscNo = GetInt(reader, "disc_no"),
        Year = GetInt(reader, "year"),
        Genre = GetString(reader, "genre"),
        MediaKindRaw = GetString(reader, "media_kind_raw"),
        ReleaseCatalogId = GetString(reader, "release_catalog_id"),
        ReleaseOrder = GetInt(reader, "release_order"),
        ArtistCatalogId = GetString(reader, "artist_catalog_id"),
        LyricsOffsetMs = GetInt(reader, "lyrics_offset_ms"),
        ReplayGain = GetDouble(reader, "replay_gain"),
        SampleRate = GetInt(reader, "sample_rate"),
        BitDepth = GetInt(reader, "bit_depth"),
        Codec = GetString(reader, "codec"),
        BitRate = GetInt(reader, "bit_rate"),
        Channels = GetInt(reader, "channels"),
        IsLossless = reader.GetInt32(reader.GetOrdinal("is_lossless")) != 0,
        MetadataStatusRaw = GetString(reader, "metadata_status_raw") ?? "embedded",
        AvailabilityRaw = GetString(reader, "availability_raw") ?? "available"
    };

    private static void BindTrack(SqliteCommand cmd, TrackEntity track)
    {
        cmd.Parameters.AddWithValue("$id", track.Id.ToString());
        cmd.Parameters.AddWithValue("$title", track.Title);
        cmd.Parameters.AddWithValue("$artist", track.Artist);
        cmd.Parameters.AddWithValue("$album", (object?)track.AlbumTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$albumArtist", (object?)track.AlbumArtist ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", track.DurationMs);
        cmd.Parameters.AddWithValue("$trackNo", (object?)track.TrackNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$discNo", (object?)track.DiscNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$year", (object?)track.Year ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$genre", (object?)track.Genre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$yt", track.YouTubeId);
        cmd.Parameters.AddWithValue("$kind", (object?)track.MediaKindRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rel", (object?)track.ReleaseCatalogId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$relOrd", (object?)track.ReleaseOrder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$artCat", (object?)track.ArtistCatalogId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$art", (object?)track.ArtworkUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lyrics", (object?)track.Lyrics ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lyOff", (object?)track.LyricsOffsetMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rg", (object?)track.ReplayGain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sr", (object?)track.SampleRate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bd", (object?)track.BitDepth ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$codec", (object?)track.Codec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$br", (object?)track.BitRate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ch", (object?)track.Channels ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lossless", track.IsLossless ? 1 : 0);
        cmd.Parameters.AddWithValue("$meta", track.MetadataStatusRaw);
        cmd.Parameters.AddWithValue("$avail", track.AvailabilityRaw);
        cmd.Parameters.AddWithValue("$added", track.AddedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$last", (object?)track.LastPlayedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$plays", track.PlayCount);
        cmd.Parameters.AddWithValue("$liked", track.Liked ? 1 : 0);
    }

    private static SqliteConnection OpenMemory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return conn;
    }

    private static void BackupCorrupt(string storePath)
    {
        try
        {
            if (!File.Exists(storePath)) return;
            var dir = Path.GetDirectoryName(storePath)!;
            var stem = Path.GetFileNameWithoutExtension(storePath);
            var ext = Path.GetExtension(storePath).TrimStart('.');
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
            File.Copy(storePath, Path.Combine(dir, $"{stem}-corrupt-{stamp}.{ext}"), overwrite: false);
        }
        catch
        {
            // Best-effort backup; fallback still proceeds.
        }
    }

    private static string? GetString(SqliteDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : reader.GetString(i);
    }

    private static Guid? GetGuid(SqliteDataReader reader, string name)
    {
        var raw = GetString(reader, name);
        return raw is null ? null : Guid.Parse(raw);
    }

    private static double? GetDouble(SqliteDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : reader.GetDouble(i);
    }

    private static long? GetLong(SqliteDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : reader.GetInt64(i);
    }

    private static int? GetInt(SqliteDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : reader.GetInt32(i);
    }

    // MARK: - IHistoryRepository

    public void RecordEvent(ListeningEventEntity evt)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO listening_events (id, track_id, started_at, ended_at, outcome_raw, context_summary_json, listened_ms)
                VALUES ($id, $trId, $start, $end, $outcome, $ctx, $ms);
                """;
            cmd.Parameters.AddWithValue("$id", evt.Id);
            cmd.Parameters.AddWithValue("$trId", (object?)evt.TrackId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$start", evt.StartedAt);
            cmd.Parameters.AddWithValue("$end", (object?)evt.EndedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$outcome", evt.Outcome.ToString());
            cmd.Parameters.AddWithValue("$ctx", (object?)evt.ContextSummaryJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ms", evt.ListenedMs);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ListeningEventEntity> GetEvents(long? fromStartedAt = null, long? toStartedAt = null)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            var sql = "SELECT id, track_id, started_at, ended_at, outcome_raw, context_summary_json, listened_ms FROM listening_events WHERE 1=1";
            if (fromStartedAt.HasValue)
            {
                sql += " AND started_at >= $from";
                cmd.Parameters.AddWithValue("$from", fromStartedAt.Value);
            }
            if (toStartedAt.HasValue)
            {
                sql += " AND started_at <= $to";
                cmd.Parameters.AddWithValue("$to", toStartedAt.Value);
            }
            sql += " ORDER BY started_at DESC;";
            cmd.CommandText = sql;

            var list = new List<ListeningEventEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var trackId = reader.IsDBNull(1) ? null : reader.GetString(1);
                var startedAt = reader.GetInt64(2);
                var endedAt = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3);
                var outcomeRaw = reader.IsDBNull(4) ? "Completed" : reader.GetString(4);
                _ = Enum.TryParse<ListeningOutcome>(outcomeRaw, out var outcome);
                var ctx = reader.IsDBNull(5) ? null : reader.GetString(5);
                var ms = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);

                list.Add(new ListeningEventEntity(id, trackId, startedAt, endedAt, outcome, ctx, ms));
            }
            return list;
        }
    }

    public void ClearHistory()
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM listening_events;";
            cmd.ExecuteNonQuery();
        }
    }

    // MARK: - IEQRepository

    public void SavePreset(EQPresetEntity preset)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO eq_presets (id, name, bands_json)
                VALUES ($id, $name, $bands);
                """;
            cmd.Parameters.AddWithValue("$id", preset.Id);
            cmd.Parameters.AddWithValue("$name", preset.Name);
            cmd.Parameters.AddWithValue("$bands", preset.BandsJson);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<EQPresetEntity> GetPresets()
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, bands_json FROM eq_presets ORDER BY name ASC;";
            var list = new List<EQPresetEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new EQPresetEntity(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
            return list;
        }
    }

    public void DeletePreset(string id)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM eq_presets WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // MARK: - INotesRepository

    public TrackNoteEntity? GetNote(string trackId)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, track_id, body, updated_at FROM track_notes WHERE track_id = $trId LIMIT 1;";
            cmd.Parameters.AddWithValue("$trId", trackId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            var id = reader.GetString(0);
            var trId = reader.GetString(1);
            var body = reader.GetString(2);
            var updatedAt = reader.GetInt64(3);
            return new TrackNoteEntity(id, trId, body, updatedAt, updatedAt);
        }
    }

    public void SetNote(string trackId, string content)
    {
        lock (_connection)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                DeleteNote(trackId);
                return;
            }

            using var cmd = _connection.CreateCommand();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            cmd.CommandText = """
                INSERT INTO track_notes (id, track_id, body, updated_at)
                VALUES ($id, $trId, $body, $up)
                ON CONFLICT(track_id) DO UPDATE SET
                    body = excluded.body,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$trId", trackId);
            cmd.Parameters.AddWithValue("$body", content);
            cmd.Parameters.AddWithValue("$up", now);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteNote(string trackId)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM track_notes WHERE track_id = $trId;";
            cmd.Parameters.AddWithValue("$trId", trackId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TrackBookmarkEntity> GetBookmarks(string trackId)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, track_id, timestamp_ms, COALESCE(title, label), note, created_at FROM track_bookmarks WHERE track_id = $trId ORDER BY timestamp_ms ASC;";
            cmd.Parameters.AddWithValue("$trId", trackId);
            var list = new List<TrackBookmarkEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var trId = reader.GetString(1);
                var ts = reader.GetDouble(2);
                var title = reader.IsDBNull(3) ? null : reader.GetString(3);
                var note = reader.IsDBNull(4) ? null : reader.GetString(4);
                var ca = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                list.Add(new TrackBookmarkEntity(id, trId, ts, title, note, ca));
            }
            return list;
        }
    }

    public void AddBookmark(TrackBookmarkEntity bookmark)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO track_bookmarks (id, track_id, timestamp_ms, label, title, note, created_at)
                VALUES ($id, $trId, $ts, $title, $title, $note, $ca);
                """;
            cmd.Parameters.AddWithValue("$id", bookmark.Id);
            cmd.Parameters.AddWithValue("$trId", bookmark.TrackId);
            cmd.Parameters.AddWithValue("$ts", bookmark.TimestampMs);
            cmd.Parameters.AddWithValue("$title", (object?)bookmark.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$note", (object?)bookmark.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ca", bookmark.CreatedAt);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateBookmark(string id, string? title, string? note)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE track_bookmarks SET title = $title, label = $title, note = $note WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteBookmark(string id)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM track_bookmarks WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<NoteSearchHit> SearchNotes(string query, IReadOnlyDictionary<string, string> trackTitles)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var needle = query.Trim().ToLowerInvariant();

        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, track_id, body FROM track_notes WHERE LOWER(body) LIKE '%' || $q || '%';";
            cmd.Parameters.AddWithValue("$q", needle);
            var hits = new List<NoteSearchHit>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var trId = reader.GetString(1);
                var body = reader.GetString(2);
                trackTitles.TryGetValue(trId, out var title);
                title ??= "Unknown track";

                // Snippet
                var idx = body.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                var start = Math.Max(0, idx - 40);
                var length = Math.Min(body.Length - start, 80);
                var snippet = body.Substring(start, length);

                hits.Add(new NoteSearchHit(id, trId, title, snippet));
            }
            return hits;
        }
    }

    // MARK: - IInboxRepository

    public IReadOnlyList<InboxItemEntity> GetItems()
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, track_id, COALESCE(track_title, ''), COALESCE(artist, ''), album_title,
                       COALESCE(duration_seconds, 0), COALESCE(youtube_id, ''), artwork_url,
                       created_at, COALESCE(source_raw, 'Manual'), state_raw, snoozed_until, listened_ms, notes
                FROM inbox_items
                ORDER BY created_at DESC;
                """;
            var list = new List<InboxItemEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var trId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var title = reader.GetString(2);
                var artist = reader.GetString(3);
                var album = reader.IsDBNull(4) ? null : reader.GetString(4);
                var dur = reader.GetDouble(5);
                var ytid = reader.GetString(6);
                var art = reader.IsDBNull(7) ? null : reader.GetString(7);
                var added = reader.GetInt64(8);
                var srcRaw = reader.GetString(9);
                _ = Enum.TryParse<InboxSource>(srcRaw, true, out var src);
                var stRaw = reader.GetString(10);
                _ = Enum.TryParse<InboxState>(stRaw, true, out var st);
                var snooze = reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11);
                var listened = reader.IsDBNull(12) ? (double?)null : reader.GetDouble(12);
                var notes = reader.IsDBNull(13) ? null : reader.GetString(13);

                list.Add(new InboxItemEntity(id, trId, title, artist, album, dur, ytid, art, added, src, st, snooze, listened, notes));
            }
            return list;
        }
    }

    public void SaveItem(InboxItemEntity item)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO inbox_items (id, track_id, track_title, artist, album_title,
                    duration_seconds, youtube_id, artwork_url, created_at, source_raw, state_raw,
                    snoozed_until, listened_ms, notes)
                VALUES ($id, $trId, $title, $artist, $album, $dur, $ytid, $art, $ca, $src, $st, $snooze, $listened, $notes);
                """;
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.Parameters.AddWithValue("$trId", item.TrackId);
            cmd.Parameters.AddWithValue("$title", item.TrackTitle);
            cmd.Parameters.AddWithValue("$artist", item.Artist);
            cmd.Parameters.AddWithValue("$album", (object?)item.AlbumTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dur", item.DurationSeconds);
            cmd.Parameters.AddWithValue("$ytid", item.YouTubeId);
            cmd.Parameters.AddWithValue("$art", (object?)item.ArtworkUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ca", item.AddedAt);
            cmd.Parameters.AddWithValue("$src", item.Source.ToString());
            cmd.Parameters.AddWithValue("$st", item.State.ToString());
            cmd.Parameters.AddWithValue("$snooze", (object?)item.SnoozeUntil ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$listened", (object?)item.ListenedMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)item.Notes ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteItem(string id)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM inbox_items WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateState(string id, InboxState state, long? snoozeUntil = null)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE inbox_items SET state_raw = $st, snoozed_until = $sn WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$st", state.ToString());
            cmd.Parameters.AddWithValue("$sn", (object?)snoozeUntil ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateNotes(string id, string? notes)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE inbox_items SET notes = $notes WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void RestoreDueSnoozes(long nowUnixMs)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE inbox_items
                SET state_raw = 'Unheard', snoozed_until = NULL
                WHERE state_raw = 'Snoozed' AND snoozed_until <= $now;
                """;
            cmd.Parameters.AddWithValue("$now", nowUnixMs);
            cmd.ExecuteNonQuery();
        }
    }

    // MARK: - IAutomationRepository

    public IReadOnlyList<AutomationRuleEntity> GetRules()
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, trigger_json, conditions_json, action_json, enabled, cooldown_ms, last_fired_at
                FROM automation_rules
                ORDER BY name ASC;
                """;
            var list = new List<AutomationRuleEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var trRaw = reader.GetString(2);
                _ = Enum.TryParse<AutomationTrigger>(trRaw, true, out var tr);
                var condJson = reader.IsDBNull(3) ? null : reader.GetString(3);
                var actRaw = reader.GetString(4);
                _ = Enum.TryParse<AutomationAction>(actRaw, true, out var act);
                var en = reader.GetInt32(5) != 0;
                var cd = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                var lf = reader.IsDBNull(7) ? (long?)null : reader.GetInt64(7);

                list.Add(new AutomationRuleEntity(id, name, en, tr, condJson, act, cd, lf));
            }
            return list;
        }
    }

    public void SaveRule(AutomationRuleEntity rule)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO automation_rules (id, name, trigger_json, conditions_json, action_json, enabled, cooldown_ms, last_fired_at)
                VALUES ($id, $name, $tr, $cond, $act, $en, $cd, $lf);
                """;
            cmd.Parameters.AddWithValue("$id", rule.Id);
            cmd.Parameters.AddWithValue("$name", rule.Name);
            cmd.Parameters.AddWithValue("$tr", rule.Trigger.ToString());
            cmd.Parameters.AddWithValue("$cond", (object?)rule.ConditionsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$act", rule.Action.ToString());
            cmd.Parameters.AddWithValue("$en", rule.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$cd", (object?)rule.CooldownMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lf", (object?)rule.LastFiredAt ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteRule(string id)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM automation_rules WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE automation_rules SET enabled = $en WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$en", enabled ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public void RecordFire(string id, long firedAt)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE automation_rules SET last_fired_at = $fa WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$fa", firedAt);
            cmd.ExecuteNonQuery();
        }
    }

    // MARK: - IFocusRepository

    public void SaveSession(FocusSessionEntity session)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO focus_sessions (id, started_at, planned_duration_ms, ended_at, status_raw, listening_session_id)
                VALUES ($id, $start, $dur, $end, $st, $lsId);
                """;
            cmd.Parameters.AddWithValue("$id", session.Id);
            cmd.Parameters.AddWithValue("$start", session.StartedAt);
            cmd.Parameters.AddWithValue("$dur", (object?)session.PlannedDurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$end", (object?)session.EndedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", session.Status.ToString());
            cmd.Parameters.AddWithValue("$lsId", (object?)session.ListeningSessionId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateSession(FocusSessionEntity session)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE focus_sessions SET ended_at = $end, status_raw = $st WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", session.Id);
            cmd.Parameters.AddWithValue("$end", (object?)session.EndedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", session.Status.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<FocusSessionEntity> GetSessions()
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, started_at, planned_duration_ms, ended_at, status_raw, listening_session_id FROM focus_sessions ORDER BY started_at DESC;";
            var list = new List<FocusSessionEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var start = reader.GetInt64(1);
                var dur = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var end = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3);
                var stRaw = reader.GetString(4);
                _ = Enum.TryParse<FocusStatus>(stRaw, true, out var st);
                var lsId = reader.IsDBNull(5) ? null : reader.GetString(5);
                list.Add(new FocusSessionEntity(id, start, dur, end, st, lsId));
            }
            return list;
        }
    }
}


