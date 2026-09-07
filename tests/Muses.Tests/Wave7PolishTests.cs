using System.Net;
using System.Text;
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

        var checker = new GitHubUpdateChecker(client, currentVersion: "0.1.0");
        var update = await checker.CheckForUpdatesAsync();

        Assert.True(update.HasUpdate);
        Assert.Equal("0.1.0", update.CurrentVersion);
        Assert.Equal("0.2.0", update.LatestVersion);
        Assert.Equal("Muses 0.2.0: Equalizer & Focus Mode", update.ReleaseTitle);
        Assert.Contains("32-band EQ", update.ReleaseNotes);
        Assert.Equal("https://github.com/xiaotwu/Muses-Euterpe/releases/download/v0.2.0/Muses-0.2.0-x64.msix", update.DownloadUrl);
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
    public void AppxManifest_ExistsAndContainsRequiredElements()
    {
        // Locate Package.appxmanifest
        var baseDir = AppContext.BaseDirectory;
        // Search up until repo root is found
        var dir = new DirectoryInfo(baseDir);
        string? manifestPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Muses.App", "Package.appxmanifest");
            if (File.Exists(candidate))
            {
                manifestPath = candidate;
                break;
            }
            dir = dir.Parent;
        }

        Assert.NotNull(manifestPath);
        var xml = File.ReadAllText(manifestPath);
        Assert.Contains("xiaotwu.Muses", xml);
        Assert.Contains("muses", xml);
        Assert.Contains("Square150x150Logo.png", xml);
        Assert.Contains("Square44x44Logo.png", xml);
        Assert.Contains("Windows.Desktop", xml);
    }

    [Fact]
    public void PackagingAssets_ExistWithNonZeroSize()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        string? assetsDir = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Muses.App", "Assets", "Packaging");
            if (Directory.Exists(candidate))
            {
                assetsDir = candidate;
                break;
            }
            dir = dir.Parent;
        }

        Assert.NotNull(assetsDir);
        var requiredFiles = new[]
        {
            "Square44x44Logo.png",
            "Square150x150Logo.png",
            "Wide310x150Logo.png",
            "StoreLogo.png",
            "SplashScreen.png"
        };

        foreach (var file in requiredFiles)
        {
            var path = Path.Combine(assetsDir, file);
            Assert.True(File.Exists(path), $"File {file} should exist in {assetsDir}");
            var length = new FileInfo(path).Length;
            Assert.True(length > 0, $"File {file} should not be empty");
        }
    }

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
