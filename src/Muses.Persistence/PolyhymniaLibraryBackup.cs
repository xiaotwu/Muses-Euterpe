using Microsoft.Data.Sqlite;
using Muses.Core.Library;
using Muses.Core.YouTube;

namespace Muses.Persistence;

/// <summary>Reads the library entities from Polyhymnia's SwiftData SQLite backup.</summary>
internal sealed class PolyhymniaLibraryBackup
{
    internal sealed record Track(long Key, TrackEntity Entity);
    internal sealed record Playlist(Guid Id, string Name, DateTimeOffset CreatedAt, bool Pinned, YouTubePlaylist? YouTube)
    {
        public List<Entry> Entries { get; } = [];
    }

    internal sealed record YouTubePlaylist(string Id, string Channel, string? ArtworkUrl);
    internal sealed record Entry(Guid Id, int Order, long? TrackKey, YouTubeEntry? YouTube);
    internal sealed record YouTubeEntry(string VideoId, string Title, string? ResourceId, TrackEntity? UnlinkedTrack);

    private static readonly DateTimeOffset AppleEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public List<Track> Tracks { get; } = [];
    public List<Playlist> Playlists { get; } = [];

    public static PolyhymniaLibraryBackup Read(string path, CancellationToken cancellationToken)
    {
        // A standalone SQLite backup has already incorporated any committed WAL frames.
        if (HasContents(path + "-wal") || HasContents(path + "-journal"))
            throw new InvalidDataException("Create a standalone SQLite backup of the Mac library first.");

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = new Uri(path).AbsoluteUri + "?immutable=1",
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using var configuration = connection.CreateCommand();
            configuration.CommandText = "PRAGMA trusted_schema = OFF;";
            configuration.ExecuteNonQuery();

            var backup = new PolyhymniaLibraryBackup();
            backup.ReadTracks(connection, cancellationToken);
            backup.ReadPlaylists(connection, linked: false, cancellationToken);
            backup.ReadPlaylists(connection, linked: true, cancellationToken);
            return backup;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("This file is not a supported Muses for Mac library backup.", ex);
        }
    }

    private void ReadTracks(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var table = new Table(connection, "ZTRACK");
        using var rows = table.Read(
            "Z_PK ZID ZYOUTUBEID ZTITLE ZARTIST ZDURATIONMS ZADDEDAT ZLIKED",
            """
            ZALBUMTITLE ZALBUMARTIST ZTRACKNO ZDISCNO ZYEAR ZGENRE ZMEDIAKINDRAW
            ZRELEASECATALOGID ZRELEASEORDER ZARTISTCATALOGID ZARTWORKURL ZLYRICS
            ZLYRICSOFFSETMS ZREPLAYGAIN ZSAMPLERATE ZBITDEPTH ZCODEC ZBITRATE ZCHANNELS
            ZISLOSSLESS ZMETADATASTATUSRAW ZAVAILABILITYRAW ZLASTPLAYEDAT ZPLAYCOUNT ZLIBRARYMEMBER
            """);
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var videoId = rows.GetString(rows.GetOrdinal("ZYOUTUBEID"));
            if (!IsVideoId(videoId)) continue;
            Tracks.Add(new Track(rows.GetInt64(0), new TrackEntity
            {
                Id = ReadId(rows),
                YouTubeId = videoId,
                Title = rows.GetString(rows.GetOrdinal("ZTITLE")),
                Artist = rows.GetString(rows.GetOrdinal("ZARTIST")),
                DurationMs = rows.GetInt32(rows.GetOrdinal("ZDURATIONMS")),
                AddedAt = ReadDate(rows, "ZADDEDAT") ?? throw new InvalidDataException("Track date is missing."),
                Liked = Integer(rows, "ZLIKED") != 0,
                AlbumTitle = String(rows, "ZALBUMTITLE"),
                AlbumArtist = String(rows, "ZALBUMARTIST"),
                TrackNo = Integer(rows, "ZTRACKNO"),
                DiscNo = Integer(rows, "ZDISCNO"),
                Year = Integer(rows, "ZYEAR"),
                Genre = String(rows, "ZGENRE"),
                MediaKindRaw = String(rows, "ZMEDIAKINDRAW"),
                ReleaseCatalogId = String(rows, "ZRELEASECATALOGID"),
                ReleaseOrder = Integer(rows, "ZRELEASEORDER"),
                ArtistCatalogId = String(rows, "ZARTISTCATALOGID"),
                ArtworkUrl = String(rows, "ZARTWORKURL"),
                Lyrics = String(rows, "ZLYRICS"),
                LyricsOffsetMs = Integer(rows, "ZLYRICSOFFSETMS"),
                ReplayGain = Number(rows, "ZREPLAYGAIN"),
                SampleRate = Integer(rows, "ZSAMPLERATE"),
                BitDepth = Integer(rows, "ZBITDEPTH"),
                Codec = String(rows, "ZCODEC"),
                BitRate = Integer(rows, "ZBITRATE"),
                Channels = Integer(rows, "ZCHANNELS"),
                IsLossless = (Integer(rows, "ZISLOSSLESS") ?? 0) != 0,
                MetadataStatusRaw = String(rows, "ZMETADATASTATUSRAW") ?? "embedded",
                AvailabilityRaw = String(rows, "ZAVAILABILITYRAW") ?? "available",
                LastPlayedAt = ReadDate(rows, "ZLASTPLAYEDAT"),
                PlayCount = Integer(rows, "ZPLAYCOUNT") ?? 0,
                LibraryMember = Integer(rows, "ZLIBRARYMEMBER") != 0
            }));
        }
    }

    private void ReadPlaylists(SqliteConnection connection, bool linked, CancellationToken cancellationToken)
    {
        using var lists = new Table(connection, linked ? "ZYOUTUBEIMPORT" : "ZPLAYLIST");
        using var items = new Table(connection, linked ? "ZYOUTUBEIMPORTITEM" : "ZPLAYLISTITEM");
        if (linked && !lists.Exists && !items.Exists) return;

        var dateColumn = linked ? "ZIMPORTEDAT" : "ZCREATEDAT";
        var playlists = new Dictionary<long, Playlist>();
        using (var rows = lists.Read(linked ? "Z_PK ZID ZTITLE ZIMPORTEDAT ZPLAYLISTID ZCHANNEL" : "Z_PK ZID ZNAME ZCREATEDAT ZPINNED",
                   linked ? "ZDELETEDAT ZARTWORKURL" : ""))
        {
            while (rows.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (linked && !rows.IsDBNull(rows.GetOrdinal("ZDELETEDAT"))) continue;
                var createdAt = ReadDate(rows, dateColumn) ?? throw new InvalidDataException("Playlist date is missing.");
                var youTube = linked
                    ? new YouTubePlaylist(rows.GetString(rows.GetOrdinal("ZPLAYLISTID")), rows.GetString(rows.GetOrdinal("ZCHANNEL")),
                        String(rows, "ZARTWORKURL"))
                    : null;
                if (youTube is { Id.Length: 0 })
                    throw new InvalidDataException("A linked Mac playlist has no YouTube playlist ID.");
                var playlist = new Playlist(ReadId(rows), rows.GetString(2), createdAt,
                    !linked && (Integer(rows, "ZPINNED") ?? 0) != 0, youTube);
                playlists.Add(rows.GetInt64(0), playlist);
                Playlists.Add(playlist);
            }
        }

        var ownerColumn = linked ? (items.HasColumn("ZIMPORT_") ? "ZIMPORT_" : "ZIMPORT") : "ZPLAYLIST";
        using var entries = items.Read($"ZID ZORDER ZTRACK {ownerColumn}" +
            (linked ? " ZYOUTUBEID ZTITLE ZARTIST ZDURATIONMS" : ""), linked ? "ZAVAILABILITYRAW ZPLAYLISTITEMID" : "");
        while (entries.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = entries.GetOrdinal(ownerColumn);
            if (entries.IsDBNull(owner) || !playlists.TryGetValue(entries.GetInt64(owner), out var playlist)) continue;

            var trackColumn = entries.GetOrdinal("ZTRACK");
            long? trackKey = entries.IsDBNull(trackColumn) ? null : entries.GetInt64(trackColumn);
            playlist.Entries.Add(new Entry(ReadId(entries), entries.GetInt32(1), trackKey,
                linked ? ReadYouTubeEntry(entries, playlist.CreatedAt) : null));
        }
    }

    private static YouTubeEntry ReadYouTubeEntry(SqliteDataReader row, DateTimeOffset addedAt)
    {
        var videoId = String(row, "ZYOUTUBEID") ?? "";
        var title = String(row, "ZTITLE") ?? videoId;
        var resourceId = String(row, "ZPLAYLISTITEMID");
        var track = IsVideoId(videoId) ? new TrackEntity
        {
            YouTubeId = videoId,
            Title = title,
            Artist = String(row, "ZARTIST") ?? "",
            DurationMs = Integer(row, "ZDURATIONMS") ?? 0,
            ArtworkUrl = YouTubeLinks.ThumbnailUrl(videoId),
            AddedAt = addedAt,
            LibraryMember = false,
            AvailabilityRaw = String(row, "ZAVAILABILITYRAW") is "deleted" or "private" or "regionBlocked"
                ? "unavailable" : "available"
        } : null;
        return new YouTubeEntry(videoId, title, resourceId, track);
    }

    private static bool IsVideoId(string id) =>
        id.Length == 11 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool HasContents(string path) => File.Exists(path) && new FileInfo(path).Length != 0;

    private static Guid ReadId(SqliteDataReader row) => row["ZID"] switch
    {
        byte[] { Length: 16 } bytes => new Guid(bytes, bigEndian: true),
        string id when Guid.TryParse(id, out var parsed) => parsed,
        _ => throw new InvalidDataException("A Mac library record has an invalid UUID.")
    };

    private static string? String(SqliteDataReader row, string name)
    {
        var column = row.GetOrdinal(name);
        return row.IsDBNull(column) ? null : row.GetString(column);
    }

    private static int? Integer(SqliteDataReader row, string name)
    {
        var column = row.GetOrdinal(name);
        return row.IsDBNull(column) ? null : row.GetInt32(column);
    }

    private static double? Number(SqliteDataReader row, string name)
    {
        var column = row.GetOrdinal(name);
        return row.IsDBNull(column) ? null : row.GetDouble(column);
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader row, string name) =>
        Number(row, name) is { } seconds ? AppleEpoch.AddSeconds(seconds) : null;

    private sealed class Table : IDisposable
    {
        private readonly SqliteCommand _command;
        private readonly string _name;
        private readonly HashSet<string> _columns = new(StringComparer.OrdinalIgnoreCase);

        public Table(SqliteConnection connection, string name)
        {
            _name = name;
            using var schema = connection.CreateCommand();
            schema.CommandText = """
                SELECT name FROM pragma_table_info($table)
                WHERE EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = $table);
                """;
            schema.Parameters.AddWithValue("$table", name);
            using var rows = schema.ExecuteReader();
            while (rows.Read()) _columns.Add(rows.GetString(0));
            _command = connection.CreateCommand();
        }

        public bool Exists => _columns.Count > 0;

        public bool HasColumn(string name) => _columns.Contains(name);

        public SqliteDataReader Read(string required, string optional)
        {
            var requiredColumns = required.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!requiredColumns.All(HasColumn))
                throw new InvalidDataException($"The Mac library backup has an unsupported {_name} schema.");

            var optionalColumns = optional.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(column => HasColumn(column) ? column : $"NULL AS {column}");
            _command.CommandText = $"SELECT {string.Join(", ", requiredColumns.Concat(optionalColumns))} FROM {_name};";
            return _command.ExecuteReader();
        }

        public void Dispose() => _command.Dispose();
    }
}
