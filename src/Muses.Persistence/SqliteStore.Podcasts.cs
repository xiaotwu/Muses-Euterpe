using Microsoft.Data.Sqlite;
using Muses.Core.Podcasts;

namespace Muses.Persistence;

public sealed partial class SqliteStore : IPodcastRepository
{
    public void FollowShow(PodcastShow show)
    {
        ValidateShowId(show.CatalogId);
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO podcast_shows (catalog_id, title, artwork_url, followed_at)
                VALUES ($id, $title, $artwork, $followed)
                ON CONFLICT (catalog_id) DO UPDATE SET
                    title = excluded.title,
                    artwork_url = COALESCE(excluded.artwork_url, podcast_shows.artwork_url);
                """;
            cmd.Parameters.AddWithValue("$id", show.CatalogId);
            cmd.Parameters.AddWithValue("$title", show.Title);
            cmd.Parameters.AddWithValue("$artwork", (object?)show.ArtworkUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$followed", show.FollowedAt.ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }
    }

    public void UnfollowShow(string catalogId)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM podcast_shows WHERE catalog_id = $id;";
            cmd.Parameters.AddWithValue("$id", catalogId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PodcastShow> FollowedShows()
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT catalog_id, title, artwork_url, followed_at
                FROM podcast_shows
                ORDER BY followed_at DESC, catalog_id;
                """;
            using var reader = cmd.ExecuteReader();
            var shows = new List<PodcastShow>();
            while (reader.Read())
            {
                shows.Add(new PodcastShow(reader.GetString(0), reader.GetString(1),
                    GetString(reader, "artwork_url"), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3))));
            }
            return shows;
        }
    }

    public void SaveEpisodes(string showCatalogId, IReadOnlyList<PodcastEpisode> episodes, int sourceOffset = 0)
    {
        ValidateShowId(showCatalogId);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        if (episodes.Count == 0) return;
        if (episodes.Any(episode => !IsPodcastVideoId(episode.VideoId)))
            throw new ArgumentException("Podcast episodes require YouTube video IDs.", nameof(episodes));

        lock (_connection)
        {
            using var transaction = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO podcast_episodes (video_id, title, artwork_url, duration_ms, published_at, availability)
                VALUES ($video, $title, $artwork, $duration, $published, $availability)
                ON CONFLICT (video_id) DO UPDATE SET
                    title = excluded.title,
                    artwork_url = COALESCE(excluded.artwork_url, podcast_episodes.artwork_url),
                    duration_ms = COALESCE(excluded.duration_ms, podcast_episodes.duration_ms),
                    published_at = COALESCE(excluded.published_at, podcast_episodes.published_at),
                    availability = excluded.availability;
                INSERT INTO podcast_episode_memberships (show_catalog_id, video_id, source_order)
                VALUES ($show, $video, $order)
                ON CONFLICT (show_catalog_id, video_id) DO UPDATE SET source_order = excluded.source_order;
                """;
            cmd.Parameters.AddWithValue("$show", showCatalogId);
            var video = cmd.Parameters.Add("$video", SqliteType.Text);
            var title = cmd.Parameters.Add("$title", SqliteType.Text);
            var artwork = cmd.Parameters.Add("$artwork", SqliteType.Text);
            var duration = cmd.Parameters.Add("$duration", SqliteType.Integer);
            var published = cmd.Parameters.Add("$published", SqliteType.Integer);
            var availability = cmd.Parameters.Add("$availability", SqliteType.Text);
            var order = cmd.Parameters.Add("$order", SqliteType.Integer);
            for (var index = 0; index < episodes.Count; index++)
            {
                var episode = episodes[index];
                video.Value = episode.VideoId;
                title.Value = episode.Title;
                artwork.Value = (object?)episode.ArtworkUrl ?? DBNull.Value;
                duration.Value = episode.DurationMs is > 0 ? episode.DurationMs.Value : DBNull.Value;
                published.Value = (object?)episode.PublishedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value;
                availability.Value = episode.Availability;
                order.Value = (long)sourceOffset + index;
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public IReadOnlyList<PodcastEpisodeState> Episodes(string showCatalogId)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT e.*
                FROM podcast_episode_memberships AS m
                JOIN podcast_episodes AS e ON e.video_id = m.video_id
                WHERE m.show_catalog_id = $show
                ORDER BY e.published_at DESC, m.source_order, e.video_id;
                """;
            cmd.Parameters.AddWithValue("$show", showCatalogId);
            using var reader = cmd.ExecuteReader();
            var episodes = new List<PodcastEpisodeState>();
            while (reader.Read())
                episodes.Add(ReadEpisode(reader));
            return episodes;
        }
    }

    public PodcastEpisodeState? Episode(string videoId)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM podcast_episodes WHERE video_id = $video;";
            cmd.Parameters.AddWithValue("$video", videoId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadEpisode(reader) : null;
        }
    }

    public IReadOnlyList<PodcastContinueEntry> ContinueEpisodes(int limit)
    {
        if (limit <= 0) return [];
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                WITH candidates AS (
                    SELECT e.*, s.catalog_id, s.title AS show_title, s.artwork_url AS show_artwork, s.followed_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY e.video_id ORDER BY s.followed_at DESC, s.catalog_id
                        ) AS membership_rank
                    FROM podcast_episodes AS e
                    JOIN podcast_episode_memberships AS m ON m.video_id = e.video_id
                    JOIN podcast_shows AS s ON s.catalog_id = m.show_catalog_id
                    WHERE e.completed = 0 AND e.last_position_ms > 0 AND e.availability = 'available'
                )
                SELECT * FROM candidates WHERE membership_rank = 1
                ORDER BY updated_at DESC, video_id LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            var entries = new List<PodcastContinueEntry>();
            while (reader.Read())
            {
                var show = new PodcastShow(reader.GetString(reader.GetOrdinal("catalog_id")),
                    reader.GetString(reader.GetOrdinal("show_title")), GetString(reader, "show_artwork"),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("followed_at"))));
                entries.Add(new PodcastContinueEntry(show, ReadEpisode(reader)));
            }
            return entries;
        }
    }

    public bool SaveEpisodeProgress(string videoId, double positionMs, int? durationMs, bool completed, DateTimeOffset updatedAt)
    {
        if (!double.IsFinite(positionMs) || positionMs < 0) return false;
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            // Use the stored duration when the engine has no duration. Once played, only
            // an explicit reset can reopen the episode. Identical checkpoints are no-ops.
            cmd.CommandText = """
                WITH effective_duration AS (
                    SELECT COALESCE($duration, duration_ms) AS duration
                    FROM podcast_episodes
                    WHERE video_id = $video AND completed = 0 AND updated_at <= $updated
                ), progress AS (
                    SELECT duration, COALESCE(MIN($position, duration), $position) AS position,
                        $completed OR COALESCE($position >= CASE
                            WHEN duration > 120000 THEN MIN(duration * 0.90, duration - 60000)
                            ELSE duration * 0.90
                        END, 0) AS played
                    FROM effective_duration
                )
                UPDATE podcast_episodes SET
                    duration_ms = progress.duration,
                    last_position_ms = CASE
                        WHEN progress.played THEN COALESCE(progress.duration, progress.position)
                        ELSE progress.position
                    END,
                    completed = progress.played,
                    played_at = CASE WHEN progress.played THEN $updated END,
                    updated_at = $updated
                FROM progress
                WHERE video_id = $video AND (
                    progress.played OR duration_ms IS NOT progress.duration OR last_position_ms IS NOT progress.position
                );
                """;
            cmd.Parameters.AddWithValue("$video", videoId);
            cmd.Parameters.AddWithValue("$position", positionMs);
            cmd.Parameters.AddWithValue("$duration", durationMs is > 0 ? durationMs.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$completed", completed);
            cmd.Parameters.AddWithValue("$updated", updatedAt.ToUnixTimeMilliseconds());
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public bool SetEpisodePlayed(string videoId, bool played, DateTimeOffset updatedAt)
    {
        lock (_connection)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE podcast_episodes SET
                    completed = $played,
                    last_position_ms = CASE WHEN $played THEN COALESCE(duration_ms, last_position_ms) ELSE 0 END,
                    played_at = CASE WHEN $played THEN $updated END,
                    updated_at = $updated
                WHERE video_id = $video AND updated_at <= $updated
                    AND (completed <> $played OR ($played = 0 AND last_position_ms <> 0));
                """;
            cmd.Parameters.AddWithValue("$video", videoId);
            cmd.Parameters.AddWithValue("$played", played);
            cmd.Parameters.AddWithValue("$updated", updatedAt.ToUnixTimeMilliseconds());
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    private static PodcastEpisodeState ReadEpisode(SqliteDataReader reader)
    {
        var episode = new PodcastEpisode(
            reader.GetString(reader.GetOrdinal("video_id")),
            reader.GetString(reader.GetOrdinal("title")),
            GetString(reader, "artwork_url"),
            GetInt(reader, "duration_ms"),
            PodcastDate(reader, "published_at"),
            reader.GetString(reader.GetOrdinal("availability")));
        return new PodcastEpisodeState(episode, reader.GetDouble(reader.GetOrdinal("last_position_ms")),
            reader.GetBoolean(reader.GetOrdinal("completed")), PodcastDate(reader, "played_at"),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("updated_at"))));
    }

    private static DateTimeOffset? PodcastDate(SqliteDataReader reader, string column) =>
        GetLong(reader, column) is { } milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;

    private static void ValidateShowId(string catalogId)
    {
        if (!catalogId.StartsWith("browse:", StringComparison.Ordinal) || catalogId.Length == "browse:".Length ||
            catalogId.Any(char.IsWhiteSpace))
            throw new ArgumentException("Podcast shows require a stable YouTube browse ID.", nameof(catalogId));
    }

    private static bool IsPodcastVideoId(string videoId) =>
        videoId.Length == 11 && videoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
