using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Muses.Core.Update;
using Muses.Infrastructure.Update;
using Xunit;

namespace Muses.Tests;

public class Wave7PolishTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.0", 0)]
    [InlineData("v0.2.0", "0.1.0", 1)]
    [InlineData("0.1.0", "v0.2.0", -1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("1.0.0", "1.0.0-rc1", 1)]
    [InlineData("1.0.0-rc1", "1.0.0", -1)]
    [InlineData("v1.0.1+build123", "1.0.1", 0)]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.2", 1)]
    public void CompareSemVer_WorksCorrectly(string v1, string v2, int expectedSign)
    {
        var result = GitHubUpdateChecker.CompareSemVer(v1, v2);
        var sign = Math.Sign(result);
        Assert.Equal(expectedSign, sign);
    }

    [Theory]
    [InlineData(true, Architecture.X64, true)]
    [InlineData(true, Architecture.Arm64, false)]
    [InlineData(true, Architecture.X86, false)]
    [InlineData(false, Architecture.X64, false)]
    public async Task CheckForUpdates_OffersTheWindowsX64PackageOnly(bool isWindows, Architecture architecture, bool expectedUpdate)
    {
        const string tag = "v0.2.0";
        var update = await CheckReleases(isWindows, architecture,
        [
            Release(tag,
            [
                Asset(tag, "Muses-0.2.0-win-x64.msix"),
                Asset(tag, "Muses-0.2.0-win-arm64.msix"),
                Asset(tag, "Muses-0.2.0-macos-arm64.dmg"),
                Asset(tag, "Muses-0.2.0-win-x64.exe")
            ])
        ]);

        Assert.Equal(expectedUpdate, update.HasUpdate);
        Assert.Equal(expectedUpdate ? AssetUrl(tag, "Muses-0.2.0-win-x64.msix") : null, update.DownloadUrl);
        if (expectedUpdate)
        {
            Assert.Equal("0.1.0", update.CurrentVersion);
            Assert.Equal("0.2.0", update.LatestVersion);
            Assert.Equal("Muses v0.2.0", update.ReleaseTitle);
            Assert.Equal("Test release notes.", update.ReleaseNotes);
            Assert.Equal(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), update.PublishedAt);
        }
    }

    [Fact]
    public async Task CheckForUpdates_SkipsForeignReleasesAndSelectsNewestUsablePackage()
    {
        var update = await CheckReleases(true, Architecture.X64,
        [
            Release("v0.9.0", [Asset("v0.9.0", "Muses-0.9.0-win-arm64.msix")]),
            Release("v0.8.0", [Asset("v0.8.0", "Muses-0.8.0-win-x64.msix")], draft: true),
            Release("v0.7.0", [Asset("v0.7.0", "Muses-0.7.0-win-x64.msix")], publishedAt: null),
            Release("v0.6.0", []),
            Release("v0.5.0", [Asset("v0.5.0", "Muses-0.5.0.msix")]),
            Release("v0.2.0", [Asset("v0.2.0", "Muses-0.2.0-win-x64.msix")]),
            Release("v0.4.0", [Asset("v0.4.0", "Muses-0.4.0-win-x64.msix", state: "starter")]),
            Release("v0.3.0", [null, 42, new { name = false }, Asset("v0.3.0", "Muses-0.3.0-win-x64.msix")])
        ]);

        Assert.True(update.HasUpdate);
        Assert.Equal("0.3.0", update.LatestVersion);
        Assert.Equal(AssetUrl("v0.3.0", "Muses-0.3.0-win-x64.msix"), update.DownloadUrl);
    }

    [Theory]
    [InlineData("https://other.example/download/Muses-0.2.0-win-x64.msix")]
    [InlineData("https://github.com/other/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-win-x64.msix")]
    [InlineData("http://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-win-x64.msix")]
    [InlineData("https://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.1.0/Muses-0.2.0-win-x64.msix")]
    [InlineData("https://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-win-arm64.msix")]
    [InlineData("https://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-win-x64.msix?redirect=1")]
    [InlineData("Muses-0.2.0-win-x64.msix")]
    public async Task CheckForUpdates_RejectsNoncanonicalDownloadUrl(string url)
    {
        var update = await CheckReleases(true, Architecture.X64,
            [Release("v0.2.0", [Asset("v0.2.0", "Muses-0.2.0-win-x64.msix", url)])]);
        Assert.False(update.HasUpdate);
        Assert.Null(update.LatestVersion);
        Assert.Null(update.DownloadUrl);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.1.1")]
    public async Task CheckForUpdates_ReportsNoUpdateWhenCurrentIsEqualOrNewer(string installed)
    {
        var update = await CheckReleases(true, Architecture.X64,
            [Release("v0.1.0", [Asset("v0.1.0", "Muses-0.1.0-win-x64.msix")])], currentVersion: installed);
        Assert.False(update.HasUpdate);
        Assert.Equal("0.1.0", update.LatestVersion);
        Assert.Null(update.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdates_IgnoresPrereleasesWhenNotRequested()
    {
        object[] releases =
        [
            Release("v0.3.0-preview", [Asset("v0.3.0-preview", "Muses-0.3.0-preview-win-x64.msix")]),
            Release("v0.2.0", [Asset("v0.2.0", "Muses-0.2.0-win-x64.msix")], prerelease: true),
            Release("v0.1.0", [Asset("v0.1.0", "Muses-0.1.0-win-x64.msix")])
        ];

        var update = await CheckReleases(true, Architecture.X64, releases);
        Assert.False(update.HasUpdate);
        Assert.Equal("0.1.0", update.LatestVersion);

        var preUpdate = await CheckReleases(true, Architecture.X64, releases, includePrereleases: true);
        Assert.True(preUpdate.HasUpdate);
        Assert.Equal("0.3.0-preview", preUpdate.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SkipsMalformedVersionOrEmptyPackage()
    {
        var update = await CheckReleases(true, Architecture.X64,
        [
            Release("0.4.0", [Asset("0.4.0", "Muses-0.4.0-win-x64.msix")]),
            Release("v65536.0.0", [Asset("v65536.0.0", "Muses-65536.0.0-win-x64.msix")]),
            Release("v0.3.invalid", [Asset("v0.3.invalid", "Muses-0.3.invalid-win-x64.msix")]),
            Release("v0.2.0", [Asset("v0.2.0", "Muses-0.2.0-win-x64.msix", size: 0)]),
            Release("v0.1.1", [Asset("v0.1.1", "Muses-0.1.0-win-x64.msix")])
        ]);
        Assert.False(update.HasUpdate);
        Assert.Null(update.LatestVersion);
        Assert.Null(update.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdates_ReportsHttpAndJsonFailures()
    {
        using var unavailable = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "[]"));
        var offline = new GitHubUpdateChecker(true, Architecture.X64, unavailable);
        await Assert.ThrowsAsync<HttpRequestException>(() => offline.CheckForUpdatesAsync());

        using var invalid = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, """{"invalid":true}"""));
        var invalidResponse = new GitHubUpdateChecker(true, Architecture.X64, invalid);
        await Assert.ThrowsAsync<JsonException>(() => invalidResponse.CheckForUpdatesAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invalidResponse.CheckForUpdatesAsync(ct: new CancellationToken(canceled: true)));
    }

    private static async Task<AppUpdateInfo> CheckReleases(bool isWindows, Architecture architecture,
        object[] releases, string currentVersion = "0.1.0", bool includePrereleases = false)
    {
        using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(releases)));
        var checker = new GitHubUpdateChecker(isWindows, architecture, client, currentVersion: currentVersion);
        return await checker.CheckForUpdatesAsync(includePrereleases);
    }

    private static object Release(string tag, object?[] assets, bool draft = false, bool prerelease = false,
        string? publishedAt = "2026-09-01T12:00:00Z") => new
    {
        tag_name = tag,
        name = $"Muses {tag}",
        body = "Test release notes.",
        draft,
        prerelease,
        published_at = publishedAt,
        html_url = $"https://github.com/xiaotwu/Muses-Euterpe/releases/tag/{tag}",
        assets
    };

    private static object Asset(string tag, string name, string? url = null, string state = "uploaded",
        long size = 1024) => new { name, state, size, browser_download_url = url ?? AssetUrl(tag, name) };

    private static string AssetUrl(string tag, string name) =>
        $"https://github.com/xiaotwu/Muses-Euterpe/releases/download/{tag}/{name}";

    [Fact]
    public void AppxManifest_PreservesIdentityForX64Upgrades()
    {
        var root = LoadPackageManifest();
        var ns = root.Name.Namespace;
        var identity = Assert.Single(root.Elements(ns + "Identity"));
        Assert.Equal("xiaotwu.Muses", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=xiaotwu", (string?)identity.Attribute("Publisher"));
        Assert.Equal("x64", (string?)identity.Attribute("ProcessorArchitecture"));
        var application = Assert.Single(root.Descendants(ns + "Application"));
        Assert.Equal("App", (string?)application.Attribute("Id"));
        Assert.Equal("Muses.exe", (string?)application.Attribute("Executable"));
        var target = Assert.Single(root.Descendants(ns + "TargetDeviceFamily"));
        Assert.Equal("Windows.Desktop", (string?)target.Attribute("Name"));
    }

    [Fact]
    public void AppxManifest_ReferencesNonEmptyPackagingAssets()
    {
        var root = LoadPackageManifest();
        var storeLogo = Assert.Single(root.Descendants(root.Name.Namespace + "Logo"));
        var visualAssets = root.Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName.EndsWith("Logo", StringComparison.Ordinal) ||
                attribute.Name.LocalName == "Image")
            .Select(attribute => attribute.Value);

        foreach (var asset in visualAssets.Prepend(storeLogo.Value))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Packaging", asset.Replace('\\', '/'));
            Assert.True(File.Exists(path) && new FileInfo(path).Length > 0, $"Missing or empty package asset {asset}");
        }
    }

    private static XElement LoadPackageManifest() =>
        XElement.Load(Path.Combine(AppContext.BaseDirectory, "Packaging", "Package.appxmanifest"));

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.github.com/repos/xiaotwu/Muses-Euterpe/releases", request.RequestUri?.AbsoluteUri);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
