using System.Text.Json.Serialization;

namespace Muses.Core.Domain;

public sealed record TrackSnapshot(
    Guid Id,
    string Title,
    string Artist,
    string? AlbumTitle,
    double DurationSeconds,
    string YouTubeId,
    string? ArtworkUrl,
    int? SampleRate = null,
    int? BitDepth = null,
    string? Codec = null,
    bool IsLossless = false,
    bool Liked = false,
    string? Lyrics = null,
    double? ReplayGain = null,
    int? BitRate = null,
    int? Channels = null,
    int? LyricsOffsetMs = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TrackMediaKind MediaKind { get; init; }

    public static TrackSnapshot Test(string title, string youTubeId = "test-video", double durationSeconds = 1) =>
        new(Guid.NewGuid(), title, "a", null, durationSeconds, youTubeId, null);
}
