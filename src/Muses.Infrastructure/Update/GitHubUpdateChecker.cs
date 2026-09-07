using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Muses.Core.Update;

namespace Muses.Infrastructure.Update;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly string _repoSlug;
    private readonly string _currentVersion;

    public string CurrentVersion => _currentVersion;

    public GitHubUpdateChecker(
        HttpClient? httpClient = null,
        string repoSlug = "xiaotwu/Muses-Euterpe",
        string? currentVersion = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _repoSlug = string.IsNullOrWhiteSpace(repoSlug) ? "xiaotwu/Muses-Euterpe" : repoSlug;
        _currentVersion = currentVersion ?? GetCurrentAssemblyVersion();
    }

    private static string GetCurrentAssemblyVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateChecker).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plusIdx = info.IndexOf('+');
            return plusIdx > 0 ? info[..plusIdx] : info;
        }

        var ver = asm.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.1.0";
    }

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{_repoSlug}/releases";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Muses-UpdateChecker", _currentVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            return new AppUpdateInfo(
                CurrentVersion: _currentVersion,
                LatestVersion: _currentVersion,
                HasUpdate: false,
                ReleaseTitle: "Check failed",
                ReleaseNotes: ex.Message,
                DownloadUrl: $"https://github.com/{_repoSlug}/releases",
                PublishedAt: null);
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new AppUpdateInfo(
                CurrentVersion: _currentVersion,
                LatestVersion: _currentVersion,
                HasUpdate: false,
                ReleaseTitle: "No releases found",
                ReleaseNotes: "",
                DownloadUrl: $"https://github.com/{_repoSlug}/releases",
                PublishedAt: null);
        }

        JsonElement? targetRelease = null;
        string? targetTag = null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean())
                continue;

            var isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
            if (isPrerelease && !includePrereleases)
                continue;

            if (release.TryGetProperty("tag_name", out var tagProp))
            {
                targetRelease = release;
                targetTag = tagProp.GetString();
                break;
            }
        }

        if (targetRelease == null || string.IsNullOrWhiteSpace(targetTag))
        {
            return new AppUpdateInfo(
                CurrentVersion: _currentVersion,
                LatestVersion: _currentVersion,
                HasUpdate: false,
                ReleaseTitle: "Up to date",
                ReleaseNotes: "No eligible releases found.",
                DownloadUrl: $"https://github.com/{_repoSlug}/releases",
                PublishedAt: null);
        }

        var rel = targetRelease.Value;
        var cleanTag = targetTag.TrimStart('v', 'V');
        var title = rel.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString())
            ? nameProp.GetString()!
            : targetTag;
        var body = rel.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
        DateTimeOffset? publishedAt = rel.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var dto)
            ? dto
            : null;

        var htmlUrl = rel.TryGetProperty("html_url", out var htmlProp)
            ? htmlProp.GetString() ?? $"https://github.com/{_repoSlug}/releases"
            : $"https://github.com/{_repoSlug}/releases";

        var downloadUrl = htmlUrl;

        // Search for windows asset (.msix, .msixbundle, .exe, .zip)
        if (rel.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
        {
            string? msixUrl = null;
            string? exeUrl = null;
            string? zipUrl = null;

            foreach (var asset in assetsProp.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var assetNameProp) &&
                    asset.TryGetProperty("browser_download_url", out var assetDlProp))
                {
                    var name = assetNameProp.GetString() ?? "";
                    var dl = assetDlProp.GetString() ?? "";
                    if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
                    {
                        msixUrl = dl;
                    }
                    else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exeUrl = dl;
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = dl;
                    }
                }
            }

            downloadUrl = msixUrl ?? exeUrl ?? zipUrl ?? htmlUrl;
        }

        var hasUpdate = CompareSemVer(cleanTag, _currentVersion) > 0;

        return new AppUpdateInfo(
            CurrentVersion: _currentVersion,
            LatestVersion: cleanTag,
            HasUpdate: hasUpdate,
            ReleaseTitle: title,
            ReleaseNotes: body,
            DownloadUrl: downloadUrl,
            PublishedAt: publishedAt);
    }

    public static int CompareSemVer(string v1, string v2)
    {
        v1 = (v1 ?? "").Trim().TrimStart('v', 'V');
        v2 = (v2 ?? "").Trim().TrimStart('v', 'V');

        var (parts1, pre1) = SplitVersion(v1);
        var (parts2, pre2) = SplitVersion(v2);

        var maxLen = Math.Max(parts1.Length, parts2.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int p1 = i < parts1.Length ? parts1[i] : 0;
            int p2 = i < parts2.Length ? parts2[i] : 0;
            if (p1 > p2) return 1;
            if (p1 < p2) return -1;
        }

        // If numeric parts are equal, check prerelease:
        // A release without prerelease tag is higher than a release with a prerelease tag.
        // e.g. 1.0.0 > 1.0.0-beta
        if (string.IsNullOrEmpty(pre1) && !string.IsNullOrEmpty(pre2))
            return 1;
        if (!string.IsNullOrEmpty(pre1) && string.IsNullOrEmpty(pre2))
            return -1;

        if (!string.IsNullOrEmpty(pre1) && !string.IsNullOrEmpty(pre2))
        {
            return string.Compare(pre1, pre2, StringComparison.OrdinalIgnoreCase);
        }

        return 0;
    }

    private static (int[] Numbers, string Prerelease) SplitVersion(string version)
    {
        var dashIdx = version.IndexOf('-');
        var plusIdx = version.IndexOf('+');
        var metaIdx = dashIdx >= 0 ? dashIdx : plusIdx;

        string numPart = metaIdx >= 0 ? version[..metaIdx] : version;
        string prePart = dashIdx >= 0 ? (plusIdx > dashIdx ? version[(dashIdx + 1)..plusIdx] : version[(dashIdx + 1)..]) : "";

        var segments = numPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new int[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            int.TryParse(segments[i], out numbers[i]);
        }

        return (numbers, prePart);
    }
}
