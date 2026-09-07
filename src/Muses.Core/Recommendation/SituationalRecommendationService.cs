using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;

namespace Muses.Core.Recommendation;

public sealed record SituationalSection(
    string Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<TrackSnapshot> Items
);

public sealed class SituationalRecommendationService
{
    private readonly LibraryService _library;

    public SituationalRecommendationService(LibraryService library)
    {
        _library = library;
    }

    public IReadOnlyList<SituationalSection> GenerateRecommendations(DateTimeOffset? currentTime = null) => Compute(currentTime);

    public IReadOnlyList<SituationalSection> Compute(DateTimeOffset? currentTime = null)
    {
        var now = currentTime ?? DateTimeOffset.Now;
        var allTracks = _library.AllTracks()
            .Where(t => !string.IsNullOrEmpty(t.YouTubeId))
            .ToList();

        if (allTracks.Count == 0) return [];

        var hour = now.Hour;
        var band = hour switch
        {
            >= 5 and < 12 => Discovery.ListeningTimeBand.Morning,
            >= 12 and < 18 => Discovery.ListeningTimeBand.Afternoon,
            >= 18 and < 22 => Discovery.ListeningTimeBand.Evening,
            _ => Discovery.ListeningTimeBand.LateNight
        };

        var sections = new List<SituationalSection>();

        // 1. Time-band section
        var (bandTitle, bandSubtitle) = band switch
        {
            Discovery.ListeningTimeBand.Morning => (L10n.L10n.Tr("Good morning", "早安"), L10n.L10n.Tr("Ease into the day", "从容开启一天")),
            Discovery.ListeningTimeBand.Afternoon => (L10n.L10n.Tr("Afternoon energy", "午后能量"), L10n.L10n.Tr("Keep moving", "保持节奏")),
            Discovery.ListeningTimeBand.Evening => (L10n.L10n.Tr("Evening unwind", "傍晚放松"), L10n.L10n.Tr("Wind down", "慢下来")),
            _ => (L10n.L10n.Tr("Late-night favorites", "深夜最爱"), L10n.L10n.Tr("For the quiet hours", "献给安静的时刻"))
        };

        var scored = allTracks
            .Select(t => (Track: t, Score: ScoreTrack(t, now)))
            .OrderByDescending(x => x.Score)
            .Take(10)
            .Select(x => x.Track.ToSnapshot())
            .ToList();

        if (scored.Count > 0)
        {
            sections.Add(new SituationalSection("time-band", bandTitle, bandSubtitle, scored));
        }

        // 2. Heavy rotation
        var heavy = allTracks
            .Where(t => t.PlayCount > 0)
            .OrderByDescending(t => t.PlayCount)
            .ThenByDescending(t => t.LastPlayedAt ?? DateTimeOffset.MinValue)
            .Take(10)
            .Select(t => t.ToSnapshot())
            .ToList();

        if (heavy.Count > 0)
        {
            sections.Add(new SituationalSection(
                "recently-obsessed",
                L10n.L10n.Tr("In heavy rotation", "循环热播"),
                L10n.L10n.Tr("Your most-played tracks", "你最常听的歌曲"),
                heavy));
        }

        // 3. Rediscover (liked songs, especially older or unplayed)
        var liked = allTracks
            .Where(t => t.Liked)
            .OrderBy(t => t.LastPlayedAt ?? DateTimeOffset.MinValue)
            .Take(10)
            .Select(t => t.ToSnapshot())
            .ToList();

        if (liked.Count > 0)
        {
            sections.Add(new SituationalSection(
                "rediscover",
                L10n.L10n.Tr("Rediscover", "重温"),
                L10n.L10n.Tr("Liked songs waiting for another spin", "等待再次播放的收藏歌曲"),
                liked));
        }

        // 4. From your YouTube library
        var librarySection = allTracks
            .OrderByDescending(t => t.AddedAt)
            .Take(10)
            .Select(t => t.ToSnapshot())
            .ToList();

        if (librarySection.Count > 0)
        {
            sections.Add(new SituationalSection(
                "from-youtube",
                L10n.L10n.Tr("From your YouTube library", "来自你的 YouTube 资料库"),
                L10n.L10n.Tr("Your tracks, ranked for now", "你的曲目,按此刻排序"),
                librarySection));
        }

        return sections;
    }

    private static double ScoreTrack(TrackEntity t, DateTimeOffset now)
    {
        double listening = Math.Min(t.PlayCount, 20.0) * 1.0;
        double recency = 0;
        if (t.LastPlayedAt is { } last)
        {
            var days = (now - last).TotalDays;
            recency = 1.5 * Math.Max(0, 1.0 - days / 30.0);
        }

        double playlist = t.Liked ? 0.5 : 0;
        double overplay = 0;
        if (t.LastPlayedAt is { } lastPlayed)
        {
            var hours = (now - lastPlayed).TotalHours;
            if (hours < 2.0) overplay = 3.0 * (2.0 - hours) / 2.0;
        }

        return listening + recency + playlist - overplay;
    }
}
