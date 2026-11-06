using Microsoft.Data.Sqlite;
using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Queue;
using Muses.Persistence;

namespace Muses.Tests;

public sealed class PolyhymniaLibraryImporterTests
{
    private const string SharedVideo = "aBCdef_01-Z";
    private const string SavedVideo = "ABCdef_01-Z";
    private const string LazyVideo = "bCDfgh_02-X";
    private static readonly Guid SavedTrackId = Guid.Parse("10203040-5060-7080-90a0-b0c0d0e0f002");
    private static readonly Guid ManualPlaylistId = Guid.Parse("19203040-5060-7080-90a0-b0c0d0e0f001");
    private static readonly Guid LinkedPlaylistId = Guid.Parse("29203040-5060-7080-90a0-b0c0d0e0f001");
    private static readonly Guid DeletedPlaylistId = Guid.Parse("29203040-5060-7080-90a0-b0c0d0e0f002");

    [Fact]
    public async Task Imports_mac_tracks_and_playlist_occurrences_without_replacing_existing_data()
    {
        using var files = new ImportFiles();
        files.WriteMacBackup();
        var sourceBytes = File.ReadAllBytes(files.Source);
        using var store = files.OpenDestination();
        var existing = AddExistingTrack(store);
        var existingPlaylist = store.Create("Mac Mix");
        store.AddTrack(existingPlaylist.Id, existing.Id);
        var existingItem = Assert.Single(store.GetPlaylist(existingPlaylist.Id)!.Items);
        store.SetNote(existing.Id.ToString(), "Euterpe note");
        var queue = new QueueService { Store = store };
        queue.Play(existing.ToSnapshot(), [existing.ToSnapshot(), existing.ToSnapshot()], QueueSource.Playlist);
        queue.CheckpointPosition(existing.Id, 15_000);
        var queueBeforeImport = store.Load()!;

        var result = await new PolyhymniaLibraryImporter(files.Destination).ImportAsync(files.Source);

        Assert.Equal(2, result.TracksAdded);
        Assert.Equal(1, result.TracksUpdated);
        Assert.Equal(2, result.PlaylistsAdded);
        Assert.Equal(8, result.PlaylistEntriesAdded);
        Assert.Equal(sourceBytes, File.ReadAllBytes(files.Source));

        var shared = Assert.IsType<TrackEntity>(store.GetByYouTubeId(SharedVideo));
        Assert.Equal(existing.Id, shared.Id);
        Assert.Equal(existing.Title, shared.Title);
        Assert.Equal(existing.Artist, shared.Artist);
        Assert.Equal(existing.DurationMs, shared.DurationMs);
        Assert.Equal(existing.PlayCount, shared.PlayCount);
        Assert.Equal(existing.LastPlayedAt, shared.LastPlayedAt);
        Assert.True(shared.Liked);
        Assert.True(shared.LibraryMember);
        Assert.Equal("Euterpe note", store.GetNote(existing.Id.ToString())?.Content);
        var queueAfterImport = store.Load()!;
        Assert.Equal(queueBeforeImport.ItemsJson, queueAfterImport.ItemsJson);
        Assert.Equal(queueBeforeImport.CurrentTrackId, queueAfterImport.CurrentTrackId);
        Assert.Equal(queueBeforeImport.LastPositionMs, queueAfterImport.LastPositionMs);
        Assert.Equal(existingItem.Id, Assert.Single(store.GetPlaylist(existingPlaylist.Id)!.Items).Id);

        var saved = Assert.IsType<TrackEntity>(store.GetByYouTubeId(SavedVideo));
        Assert.Equal(SavedTrackId, saved.Id);
        Assert.Equal("第二首", saved.Title);
        Assert.Equal("Album", saved.AlbumTitle);
        Assert.Equal(212_500, saved.DurationMs);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_678_307_200_125), saved.AddedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_678_307_300_250), saved.LastPlayedAt);
        Assert.Equal(4, saved.PlayCount);
        Assert.Equal(120, saved.LyricsOffsetMs);
        Assert.True(saved.LibraryMember);

        var manual = Assert.IsType<PlaylistEntity>(store.GetPlaylist(ManualPlaylistId));
        Assert.Equal("Mac Mix", manual.Name);
        Assert.True(manual.Pinned);
        Assert.Equal(new Guid?[] { existing.Id, saved.Id, null, existing.Id }, manual.Items.Select(item => item.TrackId));
        Assert.Equal(new[] { 0, 1, 2, 3 }, manual.Items.Select(item => item.ItemOrder));
        Assert.Equal(new[]
        {
            Guid.Parse("1a203040-5060-7080-90a0-b0c0d0e0f001"),
            Guid.Parse("1a203040-5060-7080-90a0-b0c0d0e0f002"),
            Guid.Parse("1a203040-5060-7080-90a0-b0c0d0e0f003"),
            Guid.Parse("1a203040-5060-7080-90a0-b0c0d0e0f004")
        }, manual.Items.Select(item => item.Id));

        var lazy = Assert.IsType<TrackEntity>(store.GetByYouTubeId(LazyVideo));
        Assert.Equal("Playlist-only track", lazy.Title);
        Assert.False(lazy.LibraryMember);
        var linked = Assert.IsType<YouTubeImportEntity>(store.GetImport(LinkedPlaylistId));
        Assert.Equal("Online Mix", linked.Title);
        Assert.Equal("PLExampleOne", linked.PlaylistId);
        Assert.Equal("https://www.youtube.com/playlist?list=PLExampleOne", linked.Url);
        Assert.Null(linked.AccountChannelId);
        Assert.Equal(new Guid?[] { lazy.Id, existing.Id, null, lazy.Id }, linked.Items.Select(item => item.TrackId));
        Assert.Equal(new[] { LazyVideo, SharedVideo, "", LazyVideo }, linked.Items.Select(item => item.YouTubeId));
        Assert.Equal(new string?[] { "occurrence-a", "occurrence-b", "occurrence-c", "occurrence-d" },
            linked.Items.Select(item => item.PlaylistItemId));
        Assert.Equal(4, linked.Items.Select(item => item.Id).Distinct().Count());
        Assert.Null(store.GetImport(DeletedPlaylistId));
        Assert.Equal(3, store.All().Count);
    }

    [Fact]
    public async Task Retrying_the_same_backup_preserves_local_changes()
    {
        using var files = new ImportFiles();
        files.WriteMacBackup();
        using var store = files.OpenDestination();
        var existing = AddExistingTrack(store);
        var importer = new PolyhymniaLibraryImporter(files.Destination);
        await importer.ImportAsync(files.Source);
        var manual = store.GetPlaylist(ManualPlaylistId)!;
        store.RemoveItem(manual.Items[0].Id);
        store.MoveItem(manual.Id, 1, 0);
        store.Rename(manual.Id, "Renamed on Euterpe");
        store.HardDelete(LinkedPlaylistId);
        store.ToggleLike(existing.Id);
        var renamedItems = store.GetPlaylist(manual.Id)!.Items.Select(item => item.Id).ToArray();
        var copiedBackup = Path.Combine(Path.GetDirectoryName(files.Source)!, "copied.sqlite");
        File.Copy(files.Source, copiedBackup);

        var result = await importer.ImportAsync(copiedBackup);

        Assert.Equal(0, result.TracksAdded);
        Assert.Equal(0, result.TracksUpdated);
        Assert.Equal(0, result.PlaylistsAdded);
        Assert.Equal(0, result.PlaylistEntriesAdded);
        Assert.False(store.Get(existing.Id)!.Liked);
        Assert.Null(store.GetImport(LinkedPlaylistId));
        var renamed = Assert.Single(store.GetAll());
        Assert.Equal("Renamed on Euterpe", renamed.Name);
        Assert.Equal(renamedItems, renamed.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Destination_failure_rolls_back_and_can_be_retried()
    {
        using var files = new ImportFiles();
        files.WriteMacBackup();
        using var store = files.OpenDestination();
        var existing = AddExistingTrack(store);
        files.ExecuteDestination("""
            CREATE TRIGGER fail_playlist_import BEFORE INSERT ON youtube_import_items
            BEGIN
                SELECT RAISE(ABORT, 'fixture rejects playlist occurrence');
            END;
            """);
        var importer = new PolyhymniaLibraryImporter(files.Destination);

        await Assert.ThrowsAsync<IOException>(() => importer.ImportAsync(files.Source));

        var unchanged = Assert.Single(store.All());
        Assert.Equal(existing.Id, unchanged.Id);
        Assert.False(unchanged.Liked);
        Assert.False(unchanged.LibraryMember);
        Assert.Empty(store.GetAll());
        Assert.Empty(store.GetAllImports());

        files.ExecuteDestination("DROP TRIGGER fail_playlist_import;");
        var result = await importer.ImportAsync(files.Source);

        Assert.Equal(2, result.TracksAdded);
        Assert.Equal(2, result.PlaylistsAdded);
        Assert.Equal(8, result.PlaylistEntriesAdded);
        Assert.True(store.Get(existing.Id)!.Liked);
    }

    [Fact]
    public async Task Concurrent_imports_only_write_each_backup_record_once()
    {
        using var files = new ImportFiles();
        files.WriteMacBackup();
        using var store = files.OpenDestination();

        var results = await Task.WhenAll(
            new PolyhymniaLibraryImporter(files.Destination).ImportAsync(files.Source),
            new PolyhymniaLibraryImporter(files.Destination).ImportAsync(files.Source));

        Assert.Equal(3, results.Sum(result => result.TracksAdded));
        Assert.Equal(2, results.Sum(result => result.PlaylistsAdded));
        Assert.Equal(8, results.Sum(result => result.PlaylistEntriesAdded));
        Assert.Equal(3, store.All().Count);
        Assert.Single(store.GetAll());
        Assert.Single(store.GetAllImports());
    }

    [Fact]
    public async Task Keeps_existing_linked_playlist_with_the_same_youtube_playlist_id()
    {
        using var files = new ImportFiles();
        files.WriteMacBackup();
        using var store = files.OpenDestination();
        var existingTrack = AddExistingTrack(store);
        var existing = new YouTubeImportEntity
        {
            PlaylistId = "PLExampleOne",
            Url = "https://www.youtube.com/playlist?list=PLExampleOne",
            Title = "Euterpe linked playlist",
            AccountChannelId = "fixture-euterpe-owner",
            Items =
            [
                new YouTubeImportItemEntity
                {
                    TrackId = existingTrack.Id,
                    YouTubeId = existingTrack.YouTubeId,
                    Title = existingTrack.Title,
                    PlaylistItemId = "euterpe-occurrence"
                }
            ]
        };
        store.UpsertImport(existing);

        var result = await new PolyhymniaLibraryImporter(files.Destination).ImportAsync(files.Source);

        Assert.Equal(1, result.PlaylistsAdded);
        Assert.Equal(4, result.PlaylistEntriesAdded);
        var retained = Assert.Single(store.GetAllImports());
        Assert.Equal(existing.Id, retained.Id);
        Assert.Equal(existing.Title, retained.Title);
        Assert.Equal(existing.AccountChannelId, retained.AccountChannelId);
        var item = Assert.Single(retained.Items);
        Assert.Equal(existing.Items[0].Id, item.Id);
        Assert.Equal("euterpe-occurrence", item.PlaylistItemId);
    }

    [Fact]
    public async Task Rejects_an_unrelated_database_without_mutating_the_destination()
    {
        using var files = new ImportFiles();
        files.ExecuteSource("CREATE TABLE unrelated (id INTEGER PRIMARY KEY);");
        using var store = files.OpenDestination();
        var existing = AddExistingTrack(store);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PolyhymniaLibraryImporter(files.Destination).ImportAsync(files.Source));

        Assert.Equal(existing.Id, Assert.Single(store.All()).Id);
        Assert.Empty(store.GetAll());
        Assert.Empty(store.GetAllImports());
    }

    [Fact]
    public async Task Rejects_a_backup_with_pending_wal_frames_without_mutating_either_database()
    {
        using var files = new ImportFiles();
        files.WriteMacBackup();
        using var store = files.OpenDestination();
        var existing = AddExistingTrack(store);
        using var sourceWriter = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = files.Source,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        sourceWriter.Open();
        using var command = sourceWriter.CreateCommand();
        command.CommandText = """
            PRAGMA wal_autocheckpoint = 0;
            UPDATE ZTRACK SET ZTITLE = 'Pending change' WHERE Z_PK = 20;
            """;
        command.ExecuteNonQuery();
        var sourceBytes = ReadOpenFile(files.Source);
        var walPath = files.Source + "-wal";
        var walBytes = ReadOpenFile(walPath);
        Assert.NotEmpty(walBytes);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PolyhymniaLibraryImporter(files.Destination).ImportAsync(files.Source));

        Assert.Equal(sourceBytes, ReadOpenFile(files.Source));
        Assert.Equal(walBytes, ReadOpenFile(walPath));
        var unchanged = Assert.Single(store.All());
        Assert.Equal(existing.Id, unchanged.Id);
        Assert.False(unchanged.Liked);
        Assert.False(unchanged.LibraryMember);
        Assert.Empty(store.GetAll());
        Assert.Empty(store.GetAllImports());
    }

    private static byte[] ReadOpenFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static TrackEntity AddExistingTrack(SqliteStore store)
    {
        var track = new TrackEntity
        {
            YouTubeId = SharedVideo,
            Title = "Euterpe title",
            Artist = "Euterpe artist",
            DurationMs = 189_000,
            LastPlayedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000),
            PlayCount = 17,
            LibraryMember = false
        };
        store.Upsert(track);
        return track;
    }

    private sealed class ImportFiles : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("muses-polyhymnia-import-");

        public string Source => Path.Combine(_directory.FullName, "before-upgrade # 曲库.sqlite");
        public string Destination => Path.Combine(_directory.FullName, "euterpe.sqlite");

        public SqliteStore OpenDestination() => SqliteStore.Open(Destination);

        public void ExecuteSource(string sql) => Execute(Source, sql);

        public void ExecuteDestination(string sql) => Execute(Destination, sql);

        public void WriteMacBackup()
        {
            // Synthetic rows use the SwiftData table and attribute names from Polyhymnia's models.
            // UUID blobs use Swift's byte order, and dates count seconds from 2001-01-01 UTC.
            ExecuteSource("""
                PRAGMA journal_mode = WAL;
                CREATE TABLE ZTRACK (
                    Z_PK INTEGER PRIMARY KEY, Z_ENT INTEGER, Z_OPT INTEGER, ZID BLOB, ZYOUTUBEID VARCHAR,
                    ZTITLE VARCHAR, ZARTIST VARCHAR, ZALBUMTITLE VARCHAR, ZALBUMARTIST VARCHAR, ZDURATIONMS INTEGER,
                    ZARTWORKURL VARCHAR, ZLIKED INTEGER, ZLIBRARYMEMBER INTEGER, ZADDEDAT TIMESTAMP,
                    ZLASTPLAYEDAT TIMESTAMP, ZPLAYCOUNT INTEGER, ZLYRICS VARCHAR, ZLYRICSOFFSETMS INTEGER,
                    ZMEDIAKINDRAW VARCHAR, ZMETADATASTATUSRAW VARCHAR, ZAVAILABILITYRAW VARCHAR,
                    ZTRACKNO INTEGER, ZDISCNO INTEGER, ZYEAR INTEGER, ZGENRE VARCHAR, ZRELEASECATALOGID VARCHAR,
                    ZRELEASEORDER INTEGER, ZARTISTCATALOGID VARCHAR, ZREPLAYGAIN FLOAT, ZSAMPLERATE INTEGER,
                    ZBITDEPTH INTEGER, ZCODEC VARCHAR, ZBITRATE INTEGER, ZCHANNELS INTEGER, ZISLOSSLESS INTEGER
                );
                CREATE TABLE ZPLAYLIST (
                    Z_PK INTEGER PRIMARY KEY, Z_ENT INTEGER, Z_OPT INTEGER, ZID BLOB,
                    ZNAME VARCHAR, ZCREATEDAT TIMESTAMP, ZPINNED INTEGER
                );
                CREATE TABLE ZPLAYLISTITEM (
                    Z_PK INTEGER PRIMARY KEY, Z_ENT INTEGER, Z_OPT INTEGER, ZID BLOB,
                    ZPLAYLIST INTEGER, ZTRACK INTEGER, ZORDER INTEGER
                );
                CREATE TABLE ZYOUTUBEIMPORT (
                    Z_PK INTEGER PRIMARY KEY, Z_ENT INTEGER, Z_OPT INTEGER, ZID BLOB,
                    ZPLAYLISTID VARCHAR, ZURL VARCHAR, ZTITLE VARCHAR, ZCHANNEL VARCHAR, ZARTWORKURL VARCHAR,
                    ZIMPORTEDAT TIMESTAMP, ZLASTSYNCEDAT TIMESTAMP, ZDELETEDAT TIMESTAMP, ZACCOUNTCHANNELID VARCHAR
                );
                CREATE TABLE ZYOUTUBEIMPORTITEM (
                    Z_PK INTEGER PRIMARY KEY, Z_ENT INTEGER, Z_OPT INTEGER, ZID BLOB,
                    ZIMPORT_ INTEGER, ZYOUTUBEID VARCHAR, ZPLAYLISTITEMID VARCHAR, ZTITLE VARCHAR,
                    ZARTIST VARCHAR, ZDURATIONMS INTEGER, ZORDER INTEGER, ZAVAILABILITYRAW VARCHAR, ZTRACK INTEGER
                );
                INSERT INTO ZTRACK (Z_PK, ZID, ZYOUTUBEID, ZTITLE, ZARTIST, ZALBUMTITLE, ZDURATIONMS, ZLIKED,
                    ZLIBRARYMEMBER, ZADDEDAT, ZLASTPLAYEDAT, ZPLAYCOUNT, ZLYRICS, ZLYRICSOFFSETMS, ZMEDIAKINDRAW,
                    ZMETADATASTATUSRAW, ZAVAILABILITYRAW)
                VALUES
                    (10, X'102030405060708090A0B0C0D0E0F001', 'aBCdef_01-Z', 'Mac title', 'Mac artist', NULL, 100000,
                    1, 1, 700000000, NULL, 3, NULL, NULL, 'song', 'embedded', 'available'),
                    (20, X'102030405060708090A0B0C0D0E0F002', 'ABCdef_01-Z', '第二首', 'Mac artist', 'Album', 212500,
                    0, NULL, 700000000.125, 700000100.250, 4, '[00:00.00]Synthetic lyrics', 120, 'song',
                    'embedded', 'available');
                INSERT INTO ZPLAYLIST (Z_PK, ZID, ZNAME, ZCREATEDAT, ZPINNED)
                VALUES (30, X'192030405060708090A0B0C0D0E0F001', 'Mac Mix', 700000200, 1);
                INSERT INTO ZPLAYLISTITEM (Z_PK, ZID, ZPLAYLIST, ZTRACK, ZORDER)
                VALUES
                    (40, X'1A2030405060708090A0B0C0D0E0F004', 30, 10, 9),
                    (41, X'1A2030405060708090A0B0C0D0E0F002', 30, 20, 0),
                    (42, X'1A2030405060708090A0B0C0D0E0F001', 30, 10, 0),
                    (43, X'1A2030405060708090A0B0C0D0E0F003', 30, NULL, 4);
                INSERT INTO ZYOUTUBEIMPORT (Z_PK, ZID, ZPLAYLISTID, ZURL, ZTITLE, ZCHANNEL, ZIMPORTEDAT, ZDELETEDAT,
                    ZACCOUNTCHANNELID)
                VALUES
                    (50, X'292030405060708090A0B0C0D0E0F001', 'PLExampleOne', 'https://www.youtube.com/playlist?list=PLExampleOne',
                    'Online Mix', 'Fixture channel', 700000300, NULL, 'fixture-mac-owner'),
                    (51, X'292030405060708090A0B0C0D0E0F002', 'PLExampleTwo', 'https://www.youtube.com/playlist?list=PLExampleTwo',
                    'Deleted Mix', 'Fixture channel', 700000300, 700000400, NULL);
                INSERT INTO ZYOUTUBEIMPORTITEM (Z_PK, ZID, ZIMPORT_, ZYOUTUBEID, ZPLAYLISTITEMID, ZTITLE, ZARTIST,
                    ZDURATIONMS, ZORDER, ZAVAILABILITYRAW, ZTRACK)
                VALUES
                    (60, X'2A2030405060708090A0B0C0D0E0F004', 50, 'bCDfgh_02-X', 'occurrence-d', 'Playlist-only track',
                    'Mac artist', 20000, 3, 'available', NULL),
                    (61, X'2A2030405060708090A0B0C0D0E0F001', 50, 'bCDfgh_02-X', 'occurrence-a', 'Playlist-only track',
                    'Mac artist', 20000, 0, 'available', NULL),
                    (62, X'2A2030405060708090A0B0C0D0E0F002', 50, 'aBCdef_01-Z', 'occurrence-b', 'Mac title',
                    'Mac artist', 100000, 1, 'available', 10),
                    (63, X'2A2030405060708090A0B0C0D0E0F003', 50, '', 'occurrence-c', 'Private video',
                    '', 0, 2, 'private', NULL),
                    (64, X'2A2030405060708090A0B0C0D0E0F005', 51, 'cDEghi_03-Y', 'occurrence-e', 'Deleted list track',
                    'Mac artist', 20000, 0, 'available', NULL);
                """);
        }

        public void Dispose()
        {
            using var poolKey = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Destination,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            SqliteConnection.ClearPool(poolKey);
            _directory.Delete(recursive: true);
        }

        private static void Execute(string path, string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
