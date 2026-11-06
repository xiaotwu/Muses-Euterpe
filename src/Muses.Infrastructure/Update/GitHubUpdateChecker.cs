using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Muses.Core.Update;

namespace Muses.Infrastructure.Update;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly string _repoSlug;
    private readonly string _currentVersion;
    private readonly OSPlatform _platform;
    private readonly Architecture _architecture;

    public string CurrentVersion => _currentVersion;
    public static string InstalledVersion { get; } = GetCurrentAssemblyVersion();

    public GitHubUpdateChecker(
        HttpClient? httpClient = null,
        string repoSlug = "xiaotwu/Muses-Euterpe",
        string? currentVersion = null)
        : this(
            OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsLinux() ? OSPlatform.Linux : OSPlatform.OSX,
            RuntimeInformation.ProcessArchitecture, httpClient, repoSlug, currentVersion)
    {
    }

    internal GitHubUpdateChecker(
        OSPlatform platform,
        Architecture architecture,
        HttpClient? httpClient = null,
        string repoSlug = "xiaotwu/Muses-Euterpe",
        string? currentVersion = null)
    {
        _platform = platform;
        _architecture = architecture;
        _httpClient = httpClient ?? new HttpClient();
        _repoSlug = string.IsNullOrWhiteSpace(repoSlug) ? "xiaotwu/Muses-Euterpe" : repoSlug;
        _currentVersion = currentVersion ?? InstalledVersion;
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
        string? targetAsset = null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                (release.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True))
                continue;

            var isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;
            if (isPrerelease && !includePrereleases)
                continue;

            var tag = ReadString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tag) || (targetTag is not null && CompareSemVer(tag, targetTag) <= 0))
                continue;

            var (asset, hasPackages) = FindPlatformAsset(release);
            if (hasPackages && asset is null)
                continue;

            targetRelease = release;
            targetTag = tag;
            targetAsset = asset;
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
        var title = ReadString(rel, "name");
        var body = ReadString(rel, "body") ?? "";
        DateTimeOffset? publishedAt = rel.TryGetProperty("published_at", out var pubProp) &&
            pubProp.ValueKind == JsonValueKind.String && pubProp.TryGetDateTimeOffset(out var dto)
            ? dto
            : null;

        var downloadUrl = targetAsset ?? ReadString(rel, "html_url") ?? $"https://github.com/{_repoSlug}/releases";

        var hasUpdate = CompareSemVer(cleanTag, _currentVersion) > 0;

        return new AppUpdateInfo(
            CurrentVersion: _currentVersion,
            LatestVersion: cleanTag,
            HasUpdate: hasUpdate,
            ReleaseTitle: string.IsNullOrWhiteSpace(title) ? targetTag : title,
            ReleaseNotes: body,
            DownloadUrl: downloadUrl,
            PublishedAt: publishedAt);
    }

    private (string? Url, bool HasPackages) FindPlatformAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, false);

        string? bestUrl = null;
        var bestPriority = int.MaxValue;
        var hasPackages = false;
        foreach (var asset in assets.EnumerateArray())
        {
            if (ReadString(asset, "name") is not { Length: > 0 } name)
                continue;

            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is not (".msix" or ".msixbundle" or ".exe" or ".msi" or ".zip" or
                ".deb" or ".appimage" or ".rpm" or ".dmg" or ".pkg" or ".ipa" or ".apk"))
                continue;

            hasPackages = true;
            var url = ReadString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var tokens = name.ToLowerInvariant()
                .Replace("x86_64", "x64", StringComparison.Ordinal)
                .Replace("x86-64", "x64", StringComparison.Ordinal)
                .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
            var formatPriority = PackagePriority(extension, tokens);
            if (formatPriority is null)
                continue;

            var architectures = tokens.Select(ParseArchitecture).OfType<Architecture>().ToArray();
            if (architectures.Length > 0 && !architectures.Contains(_architecture))
                continue;

            var priority = formatPriority.Value * 2 + (architectures.Length == 0 ? 1 : 0);
            if (priority >= bestPriority)
                continue;

            bestPriority = priority;
            bestUrl = url;
        }

        return (bestUrl, hasPackages);
    }

    private static string? ReadString(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private int? PackagePriority(string extension, string[] tokens)
    {
        if (_platform == OSPlatform.Linux)
        {
            return extension switch
            {
                ".deb" => 0,
                ".appimage" => 1,
                _ => null
            };
        }

        if (_platform == OSPlatform.Windows)
        {
            return extension switch
            {
                ".msix" or ".msixbundle" => 0,
                ".exe" => 1,
                ".msi" => 2,
                ".zip" when !tokens.Any(token => token is "linux" or "ubuntu" or "debian" or "mac" or "macos" or "osx" or "darwin") => 3,
                _ => null
            };
        }

        return null;
    }

    private static Architecture? ParseArchitecture(string token) => token switch
    {
        "amd64" or "x64" => Architecture.X64,
        "i386" or "i686" or "x86" => Architecture.X86,
        "aarch64" or "arm64" => Architecture.Arm64,
        "armhf" or "armv7l" or "arm" => Architecture.Arm,
        _ => null
    };

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
