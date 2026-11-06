using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Muses.Core.Podcasts;

namespace Muses.Infrastructure.Discovery;

public sealed record PodcastShowCard(string CatalogId, string Title, string? ArtworkUrl);

public sealed record PodcastCatalogPage(IReadOnlyList<PodcastEpisode> Episodes, string? Next);

/// <summary>Reads the anonymous YouTube Music catalog used by Polyhymnia.</summary>
public sealed partial class YouTubePodcastCatalog
{
    private const string Origin = "https://music.youtube.com";
    private const string BrowsePrefix = "browse:";
    private static readonly HttpClient PublicClient = new(new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(25),
        MaxResponseContentBufferSize = 8 * 1024 * 1024
    };

    private readonly HttpClient _http;
    private string? _clientVersion;

    public YouTubePodcastCatalog() : this(PublicClient) { }

    public YouTubePodcastCatalog(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<PodcastShowCard>> SearchAsync(
        string query, string language, string region, CancellationToken ct)
    {
        var payload = new Dictionary<string, object> { ["query"] = query };
        using var overview = await RequestAsync("search", payload, language, region, ct).ConfigureAwait(false);
        var results = ReadPage(overview.RootElement, includeCarousels: true);
        if (results.PodcastFilter is null) return results.Shows;

        payload["params"] = results.PodcastFilter;
        using var filtered = await RequestAsync("search", payload, language, region, ct).ConfigureAwait(false);
        return ReadPage(filtered.RootElement, includeCarousels: true).Shows;
    }

    public async Task<PodcastCatalogPage> BrowseAsync(
        string catalogId, string? cursor, string language, string region, CancellationToken ct)
    {
        if (!catalogId.StartsWith(BrowsePrefix, StringComparison.Ordinal) ||
            !ValidId(catalogId[BrowsePrefix.Length..]))
            throw new ArgumentException("Invalid podcast catalog identity.", nameof(catalogId));

        var payload = new Dictionary<string, object>();
        if (cursor is null)
            payload["browseId"] = catalogId[BrowsePrefix.Length..];
        else
            payload["continuation"] = cursor;

        using var response = await RequestAsync("browse", payload, language, region, ct).ConfigureAwait(false);
        var results = ReadPage(response.RootElement, includeCarousels: false);
        return new PodcastCatalogPage(results.Episodes, results.Next);
    }

    private async Task<JsonDocument> RequestAsync(string endpoint, Dictionary<string, object> payload,
        string language, string region, CancellationToken ct)
    {
        if (_clientVersion is null)
        {
            using var bootstrap = new HttpRequestMessage(HttpMethod.Get, Origin + "/");
            SetUserAgent(bootstrap);
            using var home = await _http.SendAsync(bootstrap, ct).ConfigureAwait(false);
            home.EnsureSuccessStatusCode();
            var version = ClientVersionPattern().Match(await home.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!version.Success)
                throw new InvalidDataException("YouTube Music client version was not present.");
            _clientVersion = version.Groups[1].Value;
        }

        payload["context"] = new
        {
            client = new { clientName = "WEB_REMIX", clientVersion = _clientVersion, hl = language, gl = region }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Origin}/youtubei/v1/{endpoint}?prettyPrint=false");
        SetUserAgent(request);
        request.Headers.Add("Origin", Origin);
        request.Content = JsonContent.Create(payload);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static void SetUserAgent(HttpRequestMessage request) => request.Headers.UserAgent.ParseAdd(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");

    [GeneratedRegex(""""INNERTUBE_CLIENT_VERSION"\s*:\s*"([0-9.]+)"""", RegexOptions.CultureInvariant)]
    private static partial Regex ClientVersionPattern();

    private sealed class ParsedPage
    {
        public List<PodcastShowCard> Shows { get; } = [];
        public List<PodcastEpisode> Episodes { get; } = [];
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
        public string? PodcastFilter { get; set; }
        public string? Next { get; set; }
    }

    private static ParsedPage ReadPage(JsonElement root, bool includeCarousels)
    {
        var contents = Child(root, "contents");
        var continuation = Child(root, "continuationContents");
        var actions = Child(root, "onResponseReceivedActions");
        if (Child(root, "error").ValueKind != JsonValueKind.Undefined ||
            (contents.ValueKind == JsonValueKind.Undefined && continuation.ValueKind == JsonValueKind.Undefined &&
             actions.ValueKind != JsonValueKind.Array))
            throw new InvalidDataException("Unrecognized YouTube Music response.");

        var page = new ParsedPage();
        Visit(contents, page, includeCarousels);
        Visit(continuation, page, includeCarousels);
        foreach (var action in ArrayItems(actions))
            Visit(Child(Child(action, "appendContinuationItemsAction"), "continuationItems"), page, includeCarousels);
        return page;
    }

    private static void Visit(JsonElement node, ParsedPage page, bool includeCarousels)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) Visit(child, page, includeCarousels);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;

        // Traverse display containers only. Related show shelves and action menus
        // do not supply the current show's membership or continuation.
        foreach (var property in node.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "musicResponsiveListItemRenderer":
                    var column = ArrayItems(Child(value, "flexColumns")).FirstOrDefault();
                    var title = Child(Child(column, "musicResponsiveListItemFlexColumnRenderer"), "text");
                    ReadRow(title, Child(value, "navigationEndpoint"), Child(value, "thumbnail"), page);
                    break;
                case "musicMultiRowListItemRenderer":
                    ReadRow(Child(value, "title"), Child(value, "onTap"), Child(value, "thumbnail"), page);
                    break;
                case "musicTwoRowItemRenderer":
                    ReadRow(Child(value, "title"), Child(value, "navigationEndpoint"), Child(value, "thumbnailRenderer"), page);
                    break;
                case "musicCardShelfRenderer":
                    ReadRow(Child(value, "title"), Child(value, "navigationEndpoint"), Child(value, "thumbnail"), page);
                    Visit(value, page, includeCarousels);
                    break;
                case "chipCloudChipRenderer":
                    if (Text(Child(value, "text")) is "Podcasts" or "Podcast" or "播客")
                        page.PodcastFilter = String(Child(Child(value, "navigationEndpoint"), "searchEndpoint"), "params");
                    break;
                case "nextContinuationData":
                    page.Next ??= String(value, "continuation");
                    break;
                case "continuationCommand":
                    page.Next ??= String(value, "token");
                    break;
                case "musicCarouselShelfRenderer" when includeCarousels:
                case "tabbedSearchResultsRenderer":
                case "tabRenderer":
                case "sectionListRenderer":
                case "musicShelfRenderer":
                case "itemSectionRenderer":
                case "musicShelfContinuation":
                case "musicPlaylistShelfContinuation":
                case "musicPlaylistShelfRenderer":
                case "twoColumnBrowseResultsRenderer":
                case "singleColumnBrowseResultsRenderer":
                case "sectionListContinuation":
                case "continuationItemRenderer":
                case "chipCloudRenderer":
                case "musicHeaderRenderer":
                case "tabs":
                case "tabbedRenderer":
                case "content":
                case "contents":
                case "secondaryContents":
                case "header":
                case "chips":
                case "continuations":
                case "continuationEndpoint":
                    Visit(value, page, includeCarousels);
                    break;
            }
        }
    }

    private static void ReadRow(JsonElement titleValue, JsonElement endpoint, JsonElement thumbnail, ParsedPage page)
    {
        var title = Text(titleValue);
        if (string.IsNullOrWhiteSpace(title)) return;
        if (endpoint.ValueKind == JsonValueKind.Undefined)
            endpoint = Child(ArrayItems(Child(titleValue, "runs")).FirstOrDefault(), "navigationEndpoint");

        var browse = Child(endpoint, "browseEndpoint");
        var browseId = String(browse, "browseId");
        var browseConfig = Child(Child(browse, "browseEndpointContextSupportedConfigs"), "browseEndpointContextMusicConfig");
        if (browseId is not null && ValidId(browseId) &&
            String(browseConfig, "pageType") is "MUSIC_PAGE_TYPE_PODCAST_SHOW" or "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE")
        {
            var catalogId = BrowsePrefix + browseId;
            if (page.Seen.Add(catalogId))
                page.Shows.Add(new PodcastShowCard(catalogId, title, Artwork(thumbnail)));
        }

        var watch = Child(endpoint, "watchEndpoint");
        var videoId = String(watch, "videoId");
        var watchConfig = Child(Child(watch, "watchEndpointMusicSupportedConfigs"), "watchEndpointMusicConfig");
        if (videoId is { Length: 11 } && ValidId(videoId) &&
            String(watchConfig, "musicVideoType") == "MUSIC_VIDEO_TYPE_PODCAST_EPISODE" &&
            page.Seen.Add(videoId))
            page.Episodes.Add(new PodcastEpisode(videoId, title, Artwork(thumbnail)));
    }

    private static string? Artwork(JsonElement thumbnail)
    {
        var container = Child(Child(thumbnail, "musicThumbnailRenderer"), "thumbnail");
        var url = String(ArrayItems(Child(container, "thumbnails")).LastOrDefault(), "url");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? url : null;
    }

    private static string Text(JsonElement value) => String(value, "simpleText") ??
        string.Concat(ArrayItems(Child(value, "runs")).Select(run => String(run, "text")));

    private static string? String(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    private static IEnumerable<JsonElement> ArrayItems(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static bool ValidId(string id) =>
        id.Length is > 0 and <= 160 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
