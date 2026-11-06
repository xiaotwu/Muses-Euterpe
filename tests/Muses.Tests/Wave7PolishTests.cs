using System.Net;
using System.Runtime.InteropServices;
using System.Text;
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
    public void CompareSemVer_WorksCorrectly(string v1, string v2, int expectedSign)
    {
        var result = GitHubUpdateChecker.CompareSemVer(v1, v2);
        var sign = Math.Sign(result);
        Assert.Equal(expectedSign, sign);
    }

    [Fact]
    public async Task CheckForUpdates_FindsNewerReleaseWithMsix()
    {
        var jsonResponse = """
        [
          {
            "tag_name": "v0.2.0",
            "name": "Muses 0.2.0: Equalizer & Focus Mode",
            "body": "### New Features\n- 32-band EQ\n- Audio Nerd overlay",
            "draft": false,
            "prerelease": false,
            "published_at": "2026-09-01T12:00:00Z",
            "html_url": "https://github.com/xiaotwu/Muses-Euterpe/releases/tag/v0.2.0",
            "assets": [
              {
                "name": "Muses-0.2.0-x64.msix",
                "browser_download_url": "https://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-x64.msix"
              }
            ]
          }
        ]
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        using var client = new HttpClient(handler);

        var checker = new GitHubUpdateChecker(OSPlatform.Windows, Architecture.X64, client, currentVersion: "0.1.0");
        var update = await checker.CheckForUpdatesAsync();

        Assert.True(update.HasUpdate);
        Assert.Equal("0.1.0", update.CurrentVersion);
        Assert.Equal("0.2.0", update.LatestVersion);
        Assert.Equal("Muses 0.2.0: Equalizer & Focus Mode", update.ReleaseTitle);
        Assert.Contains("32-band EQ", update.ReleaseNotes);
        Assert.Equal("https://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-x64.msix", update.DownloadUrl);
    }

    [Theory]
    [InlineData("linux", Architecture.X64, "Muses-0.2.0-linux-x64.deb")]
    [InlineData("linux", Architecture.Arm64, "linux-aarch64.AppImage")]
    [InlineData("windows", Architecture.X64, "Muses-0.2.0-win-x64.msix")]
    [InlineData("windows", Architecture.Arm64, "win-arm64.msix")]
    [InlineData("windows", Architecture.X86, null)]
    [InlineData("linux", Architecture.X86, null)]
    [InlineData("osx", Architecture.Arm64, null)]
    public async Task CheckForUpdates_SelectsCompatiblePlatformAsset(string platform, Architecture architecture, string? expected)
    {
        var json = """
            [{"tag_name":"v0.2.0","html_url":"release-page","assets":[
              {"name":"Muses-win-x64.exe","browser_download_url":"win-x64.exe"},
              {"name":"Muses-0.2.0-win-x64.msix","browser_download_url":"Muses-0.2.0-win-x64.msix"},
              {"name":"Muses-win-arm64.msix","browser_download_url":"win-arm64.msix"},
              {"name":"Muses-x86_64.AppImage","browser_download_url":"linux-x64.AppImage"},
              {"name":"Muses-0.2.0-linux-x64.deb","browser_download_url":"Muses-0.2.0-linux-x64.deb"},
              {"name":"Muses-aarch64.AppImage","browser_download_url":"linux-aarch64.AppImage"},
              {"name":"Muses-linux-x86.zip","browser_download_url":"linux-x86.zip"},
              {"name":"Muses-macOS-arm64.zip","browser_download_url":"macos-arm64.zip"}
            ]}]
            """;

        using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, json));
        var checker = new GitHubUpdateChecker(OSPlatform.Create(platform.ToUpperInvariant()), architecture, client, currentVersion: "0.1.0");

        var update = await checker.CheckForUpdatesAsync();

        Assert.Equal(expected is not null, update.HasUpdate);
        Assert.Equal(expected ?? "https://github.com/xiaotwu/Muses-Euterpe/releases", update.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdates_SkipsForeignReleasesAndSelectsNewestUsablePackage()
    {
        var json = """
            [
              {"tag_name":"v0.5.0","assets":[{"name":"Muses-win-x64.msix","browser_download_url":"windows"}]},
              {"tag_name":"v0.2.0","assets":[{"name":"muses_0.2.0_amd64.deb","browser_download_url":"older-linux"}]},
              {"tag_name":"v0.4.0","assets":[{"name":"muses_0.4.0_amd64.deb","browser_download_url":42}]},
              {"tag_name":"v0.3.0","assets":[null, 42, {"name":false},
                {"name":"muses_0.3.0_amd64.deb","browser_download_url":"newer-linux"}]}
            ]
            """;
        using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, json));
        var checker = new GitHubUpdateChecker(OSPlatform.Linux, Architecture.X64, client, currentVersion: "0.1.0");

        var update = await checker.CheckForUpdatesAsync();

        Assert.True(update.HasUpdate);
        Assert.Equal("0.3.0", update.LatestVersion);
        Assert.Equal("newer-linux", update.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdates_ReportsNoUpdateWhenCurrentIsEqualOrNewer()
    {
        var jsonResponse = """
        [
          {
            "tag_name": "v0.1.0",
            "name": "Muses 0.1.0 Initial Release",
            "draft": false,
            "prerelease": false,
            "html_url": "https://github.com/xiaotwu/Muses-Euterpe/releases/tag/v0.1.0"
          }
        ]
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        using var client = new HttpClient(handler);

        var checker = new GitHubUpdateChecker(client, currentVersion: "0.1.0");
        var update = await checker.CheckForUpdatesAsync();

        Assert.False(update.HasUpdate);
        Assert.Equal("0.1.0", update.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_IgnoresPrereleasesWhenNotRequested()
    {
        var jsonResponse = """
        [
          {
            "tag_name": "v0.3.0-preview",
            "name": "Muses 0.3.0 Preview",
            "draft": false,
            "prerelease": true
          },
          {
            "tag_name": "v0.1.0",
            "name": "Muses 0.1.0 Stable",
            "draft": false,
            "prerelease": false
          }
        ]
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        using var client = new HttpClient(handler);

        var checker = new GitHubUpdateChecker(client, currentVersion: "0.1.0");
        var update = await checker.CheckForUpdatesAsync(includePrereleases: false);

        Assert.False(update.HasUpdate);
        Assert.Equal("0.1.0", update.LatestVersion);

        var preUpdate = await checker.CheckForUpdatesAsync(includePrereleases: true);
        Assert.True(preUpdate.HasUpdate);
        Assert.Equal("0.3.0-preview", preUpdate.LatestVersion);
    }

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
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
