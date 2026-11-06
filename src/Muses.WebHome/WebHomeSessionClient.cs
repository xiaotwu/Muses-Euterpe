using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Muses.Core.Advanced;

namespace Muses.WebHome;

internal sealed class WebHomeSessionClient(HttpClient http, IReadOnlyList<WebHomeCookie> cookies, WebHomeRequest request)
{
    private const string MusicOrigin = "https://music.youtube.com";
    private const string WebOrigin = "https://www.youtube.com";
    private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    internal const int MaximumResponseBytes = 10 * 1024 * 1024;

    public async Task<WebHomeResponse> ExecuteAsync(CancellationToken ct)
    {
        var music = await BootstrapAsync(MusicOrigin, ct).ConfigureAwait(false);
        string? channel;
        using (var menu = await PostAsync(music, "account/account_menu", ct).ConfigureAwait(false))
            channel = WebHomeIdentity.ActiveChannel(menu.RootElement);

        channel ??= await VerifyUsingWebAsync(music, ct).ConfigureAwait(false);
        if (channel != request.ExpectedChannelId)
            return WebHomeResponse.Mismatch(channel);

        if (request.Action == "probeSession")
            return WebHomeResponse.Available(channel);

        using var feed = await PostAsync(music, "browse", ct, "browseId", "FEmusic_home").ConfigureAwait(false);
        return WebHomeResponse.Available(channel) with { Sections = WebHomeFeedParser.Parse(feed.RootElement) };
    }

    private async Task<string> VerifyUsingWebAsync(BootstrapContext music, CancellationToken ct)
    {
        var web = await BootstrapAsync(WebOrigin, ct).ConfigureAwait(false);
        if (music.SessionIndex != web.SessionIndex || music.DelegatedId != web.DelegatedId)
            throw new WebHomeFailure("accountMismatch");

        using (var menu = await PostAsync(web, "account/account_menu", ct).ConfigureAwait(false))
        {
            if (WebHomeIdentity.ActiveChannel(menu.RootElement) is { } channel)
                return channel;
        }

        string handle;
        using (var accounts = await PostAsync(web, "account/accounts_list", ct).ConfigureAwait(false))
            handle = WebHomeIdentity.SelectedHandle(accounts.RootElement);
        var url = new UriBuilder(WebOrigin) { Path = "/" + handle }.Uri.AbsoluteUri;
        using var resolved = await PostAsync(web, "navigation/resolve_url", ct, "url", url).ConfigureAwait(false);
        return WebHomeIdentity.ResolvedChannel(resolved.RootElement);
    }

    private async Task<BootstrapContext> BootstrapAsync(string origin, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, origin + "/");
        using var response = await SendAsync(message, ct).ConfigureAwait(false);
        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (html.Length > 5 * 1024 * 1024)
            throw new WebHomeFailure("responseTooLarge");
        if (Regex.IsMatch(html, "<form\\b[^>]*action\\s*=\\s*[\"']https://consent\\.youtube\\.com",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new WebHomeFailure("sessionExpired");

        var sessionIndex = ConfigValue(html, "SESSION_INDEX") ?? "0";
        if (sessionIndex.Length != 1 || !char.IsAsciiDigit(sessionIndex[0]))
            throw new WebHomeFailure("identityUnavailable");

        return new BootstrapContext(origin, RequiredConfigValue(html, "INNERTUBE_API_KEY"),
            RequiredConfigValue(html, "INNERTUBE_CLIENT_VERSION"), RequiredConfigValue(html, "VISITOR_DATA"),
            sessionIndex, ConfigValue(html, "DELEGATED_SESSION_ID"));
    }

    private async Task<JsonDocument> PostAsync(
        BootstrapContext context, string endpoint, CancellationToken ct, string? key = null, string? value = null)
    {
        var web = context.Origin == WebOrigin;
        var client = new
        {
            clientName = web ? "WEB" : "WEB_REMIX",
            clientVersion = context.ClientVersion,
            hl = request.Locale,
            gl = request.Region,
            visitorData = context.VisitorData
        };
        var user = new Dictionary<string, object> { ["enableSafetyMode"] = false };
        if (context.DelegatedId is { } delegatedId)
            user["onBehalfOfUser"] = delegatedId;

        var payload = new Dictionary<string, object> { ["context"] = new { client, user } };
        if (key is not null && value is not null)
            payload[key] = value;

        var url = $"{context.Origin}/youtubei/v1/{endpoint}?key={Uri.EscapeDataString(context.ApiKey)}&prettyPrint=false";
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        message.Headers.Add("X-YouTube-Client-Name", web ? "1" : "67");
        message.Headers.Add("X-YouTube-Client-Version", context.ClientVersion);
        message.Headers.Add("X-Goog-Visitor-Id", context.VisitorData);
        message.Headers.Add("X-Goog-AuthUser", context.SessionIndex);

        using var response = await SendAsync(message, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        var uri = message.RequestUri!;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var scoped = cookies.Where(cookie => cookie.AppliesTo(uri, timestamp))
            .OrderByDescending(cookie => cookie.Path.Length).ToArray();
        var sapisid = AuthenticationCookie(scoped);
        if (sapisid is null)
            throw new WebHomeFailure("sessionExpired");
        var cookieHeader = string.Join("; ", scoped.Select(cookie => $"{cookie.Name}={cookie.Value}"));
        if (cookieHeader.Length > 64 * 1024)
            throw new WebHomeFailure("cookieSourceUnavailable");

        var unixTime = timestamp.ToString(CultureInfo.InvariantCulture);
        var digest = SHA1.HashData(Encoding.UTF8.GetBytes($"{unixTime} {sapisid} {origin}"));
        message.Headers.Add("Authorization", $"SAPISIDHASH {unixTime}_{Convert.ToHexStringLower(digest)}");
        message.Headers.Add("Cookie", cookieHeader);
        message.Headers.Add("User-Agent", DesktopUserAgent);
        message.Headers.Add("Origin", origin);
        message.Headers.Referrer = new Uri(origin + "/");
        if (message.Method == HttpMethod.Get)
            message.Headers.Add("X-Goog-AuthUser", "0");

        var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
            return response;

        var location = response.Headers.Location;
        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "sessionExpired",
            HttpStatusCode.TooManyRequests => "rateLimited",
            _ when location is { IsAbsoluteUri: true, Host: "accounts.google.com" or "consent.youtube.com" }
                => "sessionExpired",
            _ => "offline"
        };
        response.Dispose();
        throw new WebHomeFailure(code);
    }

    private static string? AuthenticationCookie(IReadOnlyList<WebHomeCookie> cookies)
    {
        foreach (var name in new[] { "SAPISID", "__Secure-3PAPISID", "__Secure-1PAPISID" })
        {
            var cookie = cookies.FirstOrDefault(cookie => cookie.Name == name && cookie.Value.Length > 0);
            if (cookie is not null)
                return cookie.Value;
        }
        return null;
    }

    private static string RequiredConfigValue(string html, string name) =>
        ConfigValue(html, name) is { Length: > 0 } value ? value : throw new WebHomeFailure("shapeChanged");

    private static string? ConfigValue(string html, string name)
    {
        var pattern = "\"" + Regex.Escape(name) + "\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|[0-9]+)";
        var match = Regex.Match(html, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        var value = match.Groups[1].Value;
        if (value.Length > 2048) throw new WebHomeFailure("shapeChanged");
        return value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value) : value;
    }

    private sealed record BootstrapContext(
        string Origin, string ApiKey, string ClientVersion, string VisitorData, string SessionIndex, string? DelegatedId);
}
