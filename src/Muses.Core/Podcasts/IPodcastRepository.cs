namespace Muses.Core.Podcasts;

public sealed record PodcastShow(string CatalogId, string Title, string? ArtworkUrl, DateTimeOffset FollowedAt);

public sealed record PodcastEpisode(
    string VideoId,
    string Title,
    string? ArtworkUrl = null,
    int? DurationMs = null,
    DateTimeOffset? PublishedAt = null,
    string Availability = "available");

public sealed record PodcastEpisodeState(
    PodcastEpisode Episode,
    double LastPositionMs,
    bool Completed,
    DateTimeOffset? PlayedAt,
    DateTimeOffset UpdatedAt);

public sealed record PodcastContinueEntry(PodcastShow Show, PodcastEpisodeState State);

public interface IPodcastRepository
{
    void FollowShow(PodcastShow show);
    void UnfollowShow(string catalogId);
    IReadOnlyList<PodcastShow> FollowedShows();
    void SaveEpisodes(string showCatalogId, IReadOnlyList<PodcastEpisode> episodes, int sourceOffset = 0);
    IReadOnlyList<PodcastEpisodeState> Episodes(string showCatalogId);
    PodcastEpisodeState? Episode(string videoId);
    IReadOnlyList<PodcastContinueEntry> ContinueEpisodes(int limit);
    bool SaveEpisodeProgress(string videoId, double positionMs, int? durationMs, bool completed, DateTimeOffset updatedAt);
    bool SetEpisodePlayed(string videoId, bool played, DateTimeOffset updatedAt);
}
