using System.Collections.Frozen;

namespace Muses.Infrastructure.YTDlp;

public sealed record YTDlpPlaylistEntry(
    string Id,
    string Title,
    string? Uploader = null,
    double? Duration = null,
    string? PlaylistTitle = null,
    string? ChannelId = null,
    string? Track = null,
    string? Album = null,
    int? ReleaseYear = null);

public interface IYTDlpBridge
{
    Task<Uri> ResolveStreamUrlAsync(string videoId, string quality, TimeSpan timeout, CancellationToken ct = default);
    async Task<ResolvedAudioStream> ResolveAudioStreamAsync(
        string videoId, string quality, TimeSpan timeout, CancellationToken ct = default) =>
        new(await ResolveStreamUrlAsync(videoId, quality, timeout, ct).ConfigureAwait(false),
            FrozenDictionary<string, string>.Empty);
    void InvalidateAudioStream(string videoId, string quality, ResolvedAudioStream stream) { }
    Task<IReadOnlyList<YTDlpPlaylistEntry>> FetchPlaylistAsync(string url, TimeSpan timeout, CancellationToken ct = default);
    Task<IReadOnlyList<YTDlpPlaylistEntry>> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct = default);
    Task<YTDlpPlaylistEntry?> FetchVideoMetadataAsync(string videoId, TimeSpan timeout, CancellationToken ct = default);
    Task<string?> VersionAsync();
}

public sealed class YTDlpException : Exception
{
    public YTDlpException(string message) : base(message) { }
}
