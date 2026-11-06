using Microsoft.Data.Sqlite;
using Muses.Core.Library;

namespace Muses.Persistence;

public sealed partial class SqliteStore
{
    internal static void BindImportedTrackInsert(SqliteCommand command, TrackEntity track)
    {
        command.CommandText = """
            INSERT INTO tracks (id, title, artist, album_title, album_artist, duration_ms, track_no, disc_no, year, genre,
                youtube_id, media_kind_raw, release_catalog_id, release_order, artist_catalog_id, artwork_url, lyrics,
                lyrics_offset_ms, replay_gain, sample_rate, bit_depth, codec, bit_rate, channels, is_lossless,
                metadata_status_raw, availability_raw, added_at, last_played_at, play_count, liked, library_member)
            VALUES ($id, $title, $artist, $album, $albumArtist, $dur, $trackNo, $discNo, $year, $genre,
                $yt, $kind, $rel, $relOrd, $artCat, $art, $lyrics, $lyOff, $rg, $sr, $bd, $codec, $br, $ch, $lossless,
                $meta, $avail, $added, $last, $plays, $liked, $libraryMember)
            ON CONFLICT(id) DO NOTHING;
            """;
        BindTrack(command, track);
    }
}
