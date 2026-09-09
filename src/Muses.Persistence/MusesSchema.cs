using Microsoft.Data.Sqlite;

namespace Muses.Persistence;

public static class MusesSchema
{
    public static void Ensure(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS tracks (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                album_title TEXT,
                album_artist TEXT,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                track_no INTEGER,
                disc_no INTEGER,
                year INTEGER,
                genre TEXT,
                youtube_id TEXT NOT NULL,
                media_kind_raw TEXT,
                release_catalog_id TEXT,
                release_order INTEGER,
                artist_catalog_id TEXT,
                artwork_url TEXT,
                lyrics TEXT,
                lyrics_offset_ms INTEGER,
                replay_gain REAL,
                sample_rate INTEGER,
                bit_depth INTEGER,
                codec TEXT,
                bit_rate INTEGER,
                channels INTEGER,
                is_lossless INTEGER NOT NULL DEFAULT 0,
                metadata_status_raw TEXT NOT NULL,
                availability_raw TEXT NOT NULL,
                added_at INTEGER NOT NULL,
                last_played_at INTEGER,
                play_count INTEGER NOT NULL DEFAULT 0,
                liked INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_tracks_youtube ON tracks(youtube_id);
            CREATE TABLE IF NOT EXISTS queue_state (
                id TEXT PRIMARY KEY,
                items_json TEXT NOT NULL,
                current_index INTEGER NOT NULL,
                up_next_json TEXT NOT NULL,
                history_json TEXT NOT NULL,
                repeat_mode_raw TEXT NOT NULL,
                shuffle INTEGER NOT NULL,
                saved_at INTEGER NOT NULL,
                current_track_id TEXT,
                last_position_ms REAL,
                groups_json TEXT
            );
            CREATE TABLE IF NOT EXISTS playlists (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                pinned INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS playlist_items (
                id TEXT PRIMARY KEY,
                playlist_id TEXT NOT NULL,
                track_id TEXT,
                item_order INTEGER NOT NULL,
                FOREIGN KEY(playlist_id) REFERENCES playlists(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS youtube_imports (
                id TEXT PRIMARY KEY,
                playlist_id TEXT NOT NULL,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                channel TEXT NOT NULL,
                artwork_url TEXT,
                imported_at INTEGER NOT NULL,
                last_synced_at INTEGER,
                account_channel_id TEXT,
                deleted_at INTEGER
            );
            CREATE TABLE IF NOT EXISTS youtube_import_items (
                id TEXT PRIMARY KEY,
                import_id TEXT NOT NULL,
                track_id TEXT,
                youtube_id TEXT NOT NULL,
                title TEXT NOT NULL,
                item_order INTEGER NOT NULL,
                playlist_item_id TEXT,
                FOREIGN KEY(import_id) REFERENCES youtube_imports(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS eq_presets (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                bands_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS listening_events (
                id TEXT PRIMARY KEY,
                track_id TEXT,
                started_at INTEGER NOT NULL,
                ended_at INTEGER,
                outcome_raw TEXT,
                context_summary_json TEXT,
                listened_ms INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS listening_sessions (
                id TEXT PRIMARY KEY,
                started_at INTEGER NOT NULL,
                ended_at INTEGER,
                status_raw TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS inbox_items (
                id TEXT PRIMARY KEY,
                track_id TEXT,
                state_raw TEXT NOT NULL,
                source_raw TEXT,
                created_at INTEGER NOT NULL,
                snoozed_until INTEGER
            );
            CREATE TABLE IF NOT EXISTS track_notes (
                id TEXT PRIMARY KEY,
                track_id TEXT NOT NULL UNIQUE,
                body TEXT NOT NULL,
                updated_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS track_bookmarks (
                id TEXT PRIMARY KEY,
                track_id TEXT NOT NULL,
                timestamp_ms INTEGER NOT NULL,
                label TEXT
            );
            CREATE TABLE IF NOT EXISTS automation_rules (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                trigger_json TEXT NOT NULL,
                conditions_json TEXT NOT NULL,
                action_json TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS focus_sessions (
                id TEXT PRIMARY KEY,
                started_at INTEGER NOT NULL,
                ended_at INTEGER,
                duration_sec INTEGER
            );
            CREATE TABLE IF NOT EXISTS catalog_releases (
                id TEXT PRIMARY KEY,
                catalog_id TEXT NOT NULL UNIQUE,
                title TEXT NOT NULL,
                artist_name TEXT,
                kind_raw TEXT,
                artwork_url TEXT,
                year INTEGER,
                artist_catalog_id TEXT,
                refreshed_at INTEGER,
                unavailable INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS catalog_artists (
                id TEXT PRIMARY KEY,
                catalog_id TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                artwork_url TEXT,
                channel_id TEXT,
                browse_id TEXT,
                biography TEXT,
                refreshed_at INTEGER,
                unavailable INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS youtube_playlist_revisions (
                id TEXT PRIMARY KEY,
                import_id TEXT NOT NULL,
                kind_raw TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS youtube_sync_operations (
                id TEXT PRIMARY KEY,
                import_id TEXT NOT NULL,
                kind_raw TEXT NOT NULL,
                state_raw TEXT NOT NULL,
                payload_json TEXT
            );
            CREATE TABLE IF NOT EXISTS preferences (
                key TEXT PRIMARY KEY,
                value_kind TEXT NOT NULL,
                value_text TEXT,
                value_num REAL,
                value_bool INTEGER
            );
            CREATE TABLE IF NOT EXISTS youtube_sync_batches (
                id TEXT PRIMARY KEY,
                import_id TEXT NOT NULL,
                started_at INTEGER NOT NULL,
                ended_at INTEGER
            );
            """;
        cmd.ExecuteNonQuery();

        // Safe column additions for pre-existing tables
        EnsureColumn(connection, "catalog_releases", "year", "INTEGER");
        EnsureColumn(connection, "catalog_releases", "artist_catalog_id", "TEXT");
        EnsureColumn(connection, "catalog_releases", "refreshed_at", "INTEGER");
        EnsureColumn(connection, "catalog_releases", "unavailable", "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(connection, "catalog_artists", "channel_id", "TEXT");
        EnsureColumn(connection, "catalog_artists", "browse_id", "TEXT");
        EnsureColumn(connection, "catalog_artists", "biography", "TEXT");
        EnsureColumn(connection, "catalog_artists", "refreshed_at", "INTEGER");
        EnsureColumn(connection, "catalog_artists", "unavailable", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "listening_events", "listened_ms", "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(connection, "track_bookmarks", "title", "TEXT");
        EnsureColumn(connection, "track_bookmarks", "note", "TEXT");
        EnsureColumn(connection, "track_bookmarks", "created_at", "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(connection, "inbox_items", "track_title", "TEXT");
        EnsureColumn(connection, "inbox_items", "artist", "TEXT");
        EnsureColumn(connection, "inbox_items", "album_title", "TEXT");
        EnsureColumn(connection, "inbox_items", "duration_seconds", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "inbox_items", "youtube_id", "TEXT");
        EnsureColumn(connection, "inbox_items", "artwork_url", "TEXT");
        EnsureColumn(connection, "inbox_items", "listened_ms", "REAL");
        EnsureColumn(connection, "inbox_items", "notes", "TEXT");

        EnsureColumn(connection, "automation_rules", "cooldown_ms", "INTEGER");
        EnsureColumn(connection, "automation_rules", "last_fired_at", "INTEGER");

        EnsureColumn(connection, "focus_sessions", "planned_duration_ms", "INTEGER");
        EnsureColumn(connection, "focus_sessions", "status_raw", "TEXT NOT NULL DEFAULT 'Active'");
        EnsureColumn(connection, "focus_sessions", "listening_session_id", "TEXT");
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string type)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists or table does not match
        }
    }
}
