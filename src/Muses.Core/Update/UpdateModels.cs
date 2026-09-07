namespace Muses.Core.Update;

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    bool HasUpdate,
    string ReleaseTitle,
    string ReleaseNotes,
    string DownloadUrl,
    DateTimeOffset? PublishedAt);

public interface IUpdateChecker
{
    string CurrentVersion { get; }
    Task<AppUpdateInfo> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken ct = default);
}
