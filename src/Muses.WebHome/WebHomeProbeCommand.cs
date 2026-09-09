using Muses.Core.Advanced;

namespace Muses.WebHome;

/// <summary>
/// Helper-side probeSession implementation. Owns the ephemeral cookie jar and never
/// scrapes inside the Avalonia process. Identity verification beyond a channel hint
/// is intentionally Unavailable until a later wave ports the full session client.
/// </summary>
public static class WebHomeProbeCommand
{
    public static async Task<WebHomeResponse> ExecuteAsync(
        WebHomeRequest request,
        string? ytdlpPath = null,
        CancellationToken cancellationToken = default)
    {
        if (request.ProtocolVersion != WebHomeProtocolVersion.Current)
            return WebHomeResponse.Unavailable("protocolMismatch", "Unsupported Web Home protocol version.");

        if (!string.Equals(request.Action, "probeSession", StringComparison.OrdinalIgnoreCase))
            return WebHomeResponse.Unavailable("malformedResponse", $"Action '{request.Action}' is not implemented in this helper build.");

        if (string.IsNullOrWhiteSpace(request.ExpectedChannelId))
            return WebHomeResponse.Unavailable("oauthRequired", "Signed-in channel id is required before probing Web Home.");

        using var jar = new WebHomeCookieJar();
        try
        {
            jar.SeedNetscapeHeader();

            var source = request.CookieSource;
            var cookiesReady = false;

            if (source.IsFileSource)
            {
                var src = source.FilePath!;
                if (!File.Exists(src))
                    return WebHomeResponse.Unavailable("cookieSourceUnavailable", "Cookie file was not found.");
                File.Copy(src, jar.CookieFilePath, overwrite: true);
                cookiesReady = jar.HasCookiesBeyondHeader();
                // Tests may place channel.txt beside the cookie file.
                var hintBeside = Path.Combine(Path.GetDirectoryName(src) ?? "", "channel.txt");
                if (File.Exists(hintBeside))
                    File.Copy(hintBeside, Path.Combine(jar.WorkspacePath, "channel.txt"), overwrite: true);
            }
            else
            {
                var browser = (source.BrowserName ?? "").Trim().ToLowerInvariant();
                if (browser is "default" or "")
                    browser = OperatingSystem.IsWindows() ? "edge" : "chrome";
                var allowed = new HashSet<string>(StringComparer.Ordinal) { "chrome", "firefox", "brave", "edge", "safari" };
                if (!allowed.Contains(browser))
                    return WebHomeResponse.Unavailable("cookieSourceUnavailable", $"Browser '{source.BrowserName}' is not supported.");

                var spec = string.IsNullOrWhiteSpace(source.BrowserProfile) ? browser : $"{browser}:{source.BrowserProfile}";
                var ytdlp = ytdlpPath ?? LocateYTDlp();
                if (ytdlp is null || ytdlp.Length == 0)
                    return WebHomeResponse.Unavailable("cookieSourceUnavailable", "yt-dlp was not found for browser cookie export.");
                cookiesReady = await WebHomeCookieJar.TryExportFromBrowserAsync(
                    ytdlp,
                    spec,
                    jar.CookieFilePath,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!cookiesReady)
                return WebHomeResponse.Unavailable("cookieSourceUnavailable", "Could not materialize an ephemeral cookie jar.");

            var channel = jar.ReadChannelHint();
            if (channel is null)
            {
                // Honest: cookies exist but identity scrape is not in this helper build.
                return WebHomeResponse.Unavailable(
                    "identityUnavailable",
                    "Cookie jar was created and will be deleted on exit; channel identity probe is not available in this build.");
            }

            if (!string.Equals(channel, request.ExpectedChannelId, StringComparison.Ordinal))
                return WebHomeResponse.Mismatch(channel);

            return WebHomeResponse.Available(channel);
        }
        finally
        {
            // Dispose deletes the workspace — cookies do not survive helper exit.
        }
    }

    private static string? LocateYTDlp()
    {
        var name = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        var baseDir = AppContext.BaseDirectory;
        var sibling = Path.Combine(baseDir, name);
        if (File.Exists(sibling)) return sibling;
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            var c = Path.Combine(p, name);
            if (File.Exists(c)) return c;
        }
        return null;
    }
}
