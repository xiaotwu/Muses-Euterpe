using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Muses.Core.Update;

namespace Muses.Infrastructure.Update;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private const string DefaultRepository = "xiaotwu/Muses-Euterpe";
    private static readonly HttpClient DefaultHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 2 * 1024 * 1024
    };
    private static readonly Regex ReleaseTag = new(
        @"\Av(?<core>(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2})(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly HttpClient _httpClient;
    private readonly string _repoSlug;
    private readonly string _currentVersion;
    private readonly OSPlatform _platform;
    private readonly Architecture _architecture;

    public string CurrentVersion => _currentVersion;
    public static string InstalledVersion { get; } = GetCurrentAssemblyVersion();

    public GitHubUpdateChecker(
        HttpClient? httpClient = null,
        string repoSlug = DefaultRepository,
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
        string repoSlug = DefaultRepository,
        string? currentVersion = null)
    {
        _platform = platform;
        _architecture = architecture;
        _httpClient = httpClient ?? DefaultHttpClient;
        _repoSlug = string.IsNullOrWhiteSpace(repoSlug) ? DefaultRepository : repoSlug;
        if (!IsRepositorySlug(_repoSlug))
            throw new ArgumentException("Expected an owner/repository name.", nameof(repoSlug));
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
        ct.ThrowIfCancellationRequested();
        var packageSuffix = GetPackageSuffix();
        if (packageSuffix is null)
            return NoCompatibleRelease();

        var url = $"https://api.github.com/repos/{_repoSlug}/releases";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("Muses-UpdateChecker")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var json = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a list of GitHub releases.");

        JsonElement? targetRelease = null;
        string? targetVersion = null;
        string? targetAsset = null;
        DateTimeOffset? publishedAt = null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("prerelease", out var prerelease) ||
                prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !release.TryGetProperty("published_at", out var published) || published.ValueKind != JsonValueKind.String ||
                !published.TryGetDateTimeOffset(out var publishedDate))
                continue;

            if (ReadString(release, "tag_name") is not { } tag ||
                !TryReadReleaseVersion(tag, out var version, out var tagIsPrerelease) ||
                (!includePrereleases && (prerelease.GetBoolean() || tagIsPrerelease)) ||
                (targetVersion is not null && CompareSemVer(version, targetVersion) <= 0))
                continue;

            var asset = FindPlatformAsset(release, tag, $"Muses-{version}-{packageSuffix}");
            if (asset is null)
                continue;

            targetRelease = release;
            targetVersion = version;
            targetAsset = asset;
            publishedAt = publishedDate;
        }

        if (targetRelease is not { } rel || targetVersion is null)
            return NoCompatibleRelease();

        var title = ReadString(rel, "name");
        var hasUpdate = CompareSemVer(targetVersion, _currentVersion) > 0;

        return new AppUpdateInfo(
            CurrentVersion: _currentVersion,
            LatestVersion: targetVersion,
            HasUpdate: hasUpdate,
            ReleaseTitle: string.IsNullOrWhiteSpace(title) ? targetVersion : title,
            ReleaseNotes: ReadString(rel, "body") ?? "",
            DownloadUrl: hasUpdate ? targetAsset : null,
            PublishedAt: publishedAt);
    }

    private AppUpdateInfo NoCompatibleRelease() => new(_currentVersion, null, false, "", "", null, null);

    private string? GetPackageSuffix()
    {
        if (_architecture != Architecture.X64)
            return null;
        if (_platform == OSPlatform.Windows)
            return "win-x64.msix";
        return _platform == OSPlatform.Linux ? "linux-x64.deb" : null;
    }

    private string? FindPlatformAsset(JsonElement release, string tag, string name)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        var expectedUrl = $"https://github.com/{_repoSlug}/releases/download/{tag}/{name}";
        foreach (var asset in assets.EnumerateArray())
        {
            if (ReadString(asset, "name") == name &&
                ReadString(asset, "state") == "uploaded" &&
                asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number &&
                size.TryGetInt64(out var bytes) && bytes > 0 &&
                ReadString(asset, "browser_download_url") == expectedUrl)
                return expectedUrl;
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static bool IsRepositorySlug(string slug)
    {
        var parts = slug.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part is not ("." or "..") &&
            part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'));
    }

    private static bool TryReadReleaseVersion(string tag, out string version, out bool isPrerelease)
    {
        var match = ReleaseTag.Match(tag);
        var prerelease = match.Groups["pre"].Value;
        isPrerelease = prerelease.Length > 0;
        version = match.Success ? tag[1..] : "";
        var core = match.Groups["core"].Value;
        return match.Success && core != "0.0.0" && Version.TryParse(core, out var parsed) &&
            parsed is { Major: <= ushort.MaxValue, Minor: <= ushort.MaxValue, Build: <= ushort.MaxValue } &&
            (!isPrerelease || !prerelease.Split('.').Any(part =>
                part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit)));
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

        return ComparePrerelease(pre1, pre2);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var a = leftParts[i];
            var b = rightParts[i];
            var aNumeric = a.Length > 0 && a.All(char.IsAsciiDigit);
            var bNumeric = b.Length > 0 && b.All(char.IsAsciiDigit);
            if (aNumeric != bNumeric)
                return aNumeric ? -1 : 1;
            if (aNumeric && a.Length != b.Length)
                return a.Length.CompareTo(b.Length);
            var difference = string.CompareOrdinal(a, b);
            if (difference != 0)
                return difference;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
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
