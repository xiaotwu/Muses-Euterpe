using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Muses.Core.Advanced;

namespace Muses.WebHome;

public static class WebHomeProbeCommand
{
    public static async Task<WebHomeResponse> ExecuteAsync(
        WebHomeRequest request,
        string? ytdlpPath = null,
        CancellationToken cancellationToken = default,
        HttpMessageHandler? handler = null)
    {
        if (request.ProtocolVersion != WebHomeProtocolVersion.Current)
            return WebHomeResponse.Unavailable("protocolMismatch");
        if (request.Action is not ("probeSession" or "fetchHome"))
            return WebHomeResponse.Unavailable("malformedRequest");
        if (string.IsNullOrWhiteSpace(request.ExpectedChannelId))
            return WebHomeResponse.Unavailable("oauthRequired");
        if (request.CookieSource is not { } source)
            return WebHomeResponse.Unavailable("cookieSourceUnavailable");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Clamp(request.TimeoutMs, 1000, 12000));
        var ct = timeout.Token;
        try
        {
            IReadOnlyList<WebHomeCookie> cookies;
            if (source.IsFileSource && handler is not null)
            {
                cookies = await WebHomeCookieJar.ReadAsync(source.FilePath!, ct).ConfigureAwait(false);
            }
            else
            {
                if (source.IsFileSource || source.BrowserProfile is not null ||
                    source.BrowserName != (OperatingSystem.IsWindows() ? "edge" : "chrome") ||
                    request.WorkspaceId is not { } workspaceId || workspaceId == Guid.Empty)
                    return WebHomeResponse.Unavailable("cookieSourceUnavailable");

                var ytdlp = ytdlpPath ?? LocateYTDlp();
                if (ytdlp is null)
                    return WebHomeResponse.Unavailable("cookieSourceUnavailable");

                using var jar = new WebHomeCookieJar(id: workspaceId);
                jar.SeedNetscapeHeader();
                if (!await WebHomeCookieJar.TryExportFromBrowserAsync(ytdlp, source.BrowserName, jar.CookieFilePath, ct)
                    .ConfigureAwait(false))
                    return WebHomeResponse.Unavailable("cookieSourceUnavailable");

                cookies = await WebHomeCookieJar.ReadAsync(jar.CookieFilePath, ct).ConfigureAwait(false);
            }

            using var http = new HttpClient(handler ?? new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All
            }, disposeHandler: handler is null)
            {
                MaxResponseContentBufferSize = WebHomeSessionClient.MaximumResponseBytes
            };
            return await new WebHomeSessionClient(http, cookies, request).ExecuteAsync(ct).ConfigureAwait(false);
        }
        catch (WebHomeFailure failure)
        {
            return WebHomeResponse.Unavailable(failure.Code);
        }
        catch (OperationCanceledException)
        {
            return WebHomeResponse.Unavailable(cancellationToken.IsCancellationRequested ? "cancelled" : "timedOut");
        }
        catch (Exception ex) when (ex is JsonException or RegexMatchTimeoutException or FormatException)
        {
            return WebHomeResponse.Unavailable("shapeChanged");
        }
        catch (HttpRequestException)
        {
            return WebHomeResponse.Unavailable("offline");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return WebHomeResponse.Unavailable("cookieSourceUnavailable");
        }
    }

    private static string? LocateYTDlp()
    {
        var name = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        var helperDir = new DirectoryInfo(AppContext.BaseDirectory);
        foreach (var dir in new[] { helperDir, helperDir.Parent })
        {
            if (dir is null) continue;
            var bundled = Path.Combine(dir.FullName, name);
            if (File.Exists(bundled)) return bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathFullyQualified)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
    }
}
