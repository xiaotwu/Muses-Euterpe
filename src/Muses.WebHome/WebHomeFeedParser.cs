using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Muses.Core.Advanced;
using Muses.Core.Discovery;

namespace Muses.WebHome;

internal static class WebHomeFeedParser
{
    private const int MaximumSections = 40;
    private const int MaximumItemsPerSection = 50;

    internal static IReadOnlyList<WebHomeSection> Parse(JsonElement response)
    {
        if (response.Get("responseContext").Get("mainAppWebResponseContext").Get("loggedOut").ValueKind == JsonValueKind.True)
            throw new WebHomeFailure("sessionExpired");

        var sections = new List<WebHomeSection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var audit = new ParseAudit();
        Visit(response);
        if (sections.Count == 0 || audit.InvalidSections > audit.ValidSections || audit.InvalidItems > audit.ValidItems)
            throw new WebHomeFailure("shapeChanged");
        return sections;

        void Visit(JsonElement node)
        {
            if (sections.Count >= MaximumSections)
                return;

            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    Visit(item);
                return;
            }

            if (node.ValueKind != JsonValueKind.Object)
                return;

            foreach (var property in node.EnumerateObject())
            {
                if (property.Name is "musicCarouselShelfRenderer" or "musicShelfRenderer" or "gridRenderer"
                    or "musicPlaylistShelfRenderer" or "musicShelfContinuation")
                {
                    var section = ParseSection(property.Name, property.Value, audit);
                    if (section is null)
                        audit.InvalidSections++;
                    else
                    {
                        audit.ValidSections++;
                        if (seen.Add(section.Id))
                            sections.Add(section);
                    }
                    if (sections.Count >= MaximumSections)
                        return;
                }
                else if (!property.Name.EndsWith("Renderer", StringComparison.Ordinal)
                    || property.Name is "singleColumnBrowseResultsRenderer" or "tabRenderer" or "sectionListRenderer")
                    Visit(property.Value);
            }
        }
    }

    private static WebHomeSection? ParseSection(string renderer, JsonElement node, ParseAudit audit)
    {
        var header = FirstProperty(node.Get("header"), "musicCarouselShelfBasicHeaderRenderer",
            "musicShelfHeaderRenderer", "gridHeaderRenderer");
        var title = header.Get("title").Text() ?? node.Get("title").Text();
        if (title is null)
            return null;

        var items = new List<WebHomeItem>();
        var seen = new HashSet<HomeCardEndpoint>();
        var responsive = false;
        foreach (var wrapper in FirstProperty(node, "contents", "items").Items())
        {
            var itemNode = FirstProperty(wrapper, "musicTwoRowItemRenderer", "musicResponsiveListItemRenderer",
                "musicMultiRowListItemRenderer");
            if (itemNode.ValueKind == JsonValueKind.Undefined)
                continue;

            var item = ParseItem(itemNode);
            if (item is null)
            {
                audit.InvalidItems++;
                continue;
            }

            audit.ValidItems++;
            responsive |= wrapper.Get("musicResponsiveListItemRenderer").ValueKind == JsonValueKind.Object;
            if (seen.Add(item.Identity))
                items.Add(item);
            if (items.Count == MaximumItemsPerSection)
                break;
        }

        if (items.Count == 0)
            return null;

        var kind = renderer switch
        {
            "musicShelfRenderer" => HomeSectionKind.SongGrid,
            "musicCarouselShelfRenderer" when responsive => HomeSectionKind.QuickPicks,
            _ when items.All(item => item.Identity.Kind == HomeCardEndpointKind.Playlist) => HomeSectionKind.PlaylistCarousel,
            _ when items.All(item => item.Identity is { Kind: HomeCardEndpointKind.Browse, Identifier: var id }
                && id.StartsWith("MPRE", StringComparison.Ordinal)) => HomeSectionKind.AlbumCarousel,
            _ => HomeSectionKind.YouTubeCarousel
        };
        var subtitle = header.Get("strapline").Text() ?? header.Get("subtitle").Text() ?? node.Get("subtitle").Text();
        var source = Endpoint(header, play: false) ?? Endpoint(node, play: false) ?? items[0].Identity;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{renderer}|{source.Kind}|{source.Identifier}|{title}"));
        return new WebHomeSection("web-" + Convert.ToHexStringLower(digest.AsSpan(0, 12)), title, subtitle, kind, items);
    }

    private static WebHomeItem? ParseItem(JsonElement node)
    {
        var columns = node.Get("flexColumns").Items()
            .Select(column => column.Get("musicResponsiveListItemFlexColumnRenderer").Get("text")).ToArray();
        var title = node.Get("title").Text() ?? columns.FirstOrDefault().Text();
        if (title is null)
            return null;

        var browse = Endpoint(node, play: false);
        var play = Endpoint(node, play: true);
        if ((play ?? browse) is not { } identity)
            return null;

        var subtitle = node.Get("subtitle").Text() ?? columns.Skip(1).Select(column => column.Text())
            .FirstOrDefault(text => text is not null);
        return new WebHomeItem(identity, title, subtitle, Artwork(node), browse, play, Availability(node));
    }

    private static HomeCardEndpoint? Endpoint(JsonElement node, bool play)
    {
        foreach (var endpoint in new[] { node.Get("navigationEndpoint"), node.Get("playNavigationEndpoint"),
            node.Get("endpoint"), node.Get("serviceEndpoint"), node })
        {
            var watch = endpoint.Get("watchEndpoint");
            if (play)
            {
                var video = endpoint.Get("playlistItemData").Get("videoId").String() ?? watch.Get("videoId").String();
                if (WebHomeJson.IsId(video))
                    return new HomeCardEndpoint(HomeCardEndpointKind.Video, video!);
                continue;
            }

            var browseId = endpoint.Get("browseEndpoint").Get("browseId").String();
            if (WebHomeJson.IsId(browseId))
            {
                if (browseId!.StartsWith("VL", StringComparison.Ordinal) && browseId.Length > 2)
                    return new HomeCardEndpoint(HomeCardEndpointKind.Playlist, browseId[2..]);
                var kind = browseId.StartsWith("UC", StringComparison.Ordinal)
                    ? HomeCardEndpointKind.Channel : HomeCardEndpointKind.Browse;
                return new HomeCardEndpoint(kind, browseId);
            }

            var playlist = watch.Get("playlistId").String() ?? endpoint.Get("watchPlaylistEndpoint").Get("playlistId").String();
            if (WebHomeJson.IsId(playlist))
                return new HomeCardEndpoint(HomeCardEndpointKind.Playlist, playlist!);
        }
        return null;
    }

    private static string? Artwork(JsonElement node)
    {
        var root = FirstProperty(node, "thumbnail", "thumbnailRenderer");
        return root.Descendants()
            .SelectMany(candidate => candidate.Get("thumbnails").Items())
            .Select(candidate => candidate.Get("url").String())
            .FirstOrDefault(IsAllowedArtwork);
    }

    private static bool IsAllowedArtwork(string? value)
    {
        if (value is not { Length: > 0 and <= 2048 } || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            return false;

        var host = uri.IdnHost;
        return new[] { "ytimg.com", "googleusercontent.com", "ggpht.com", "youtube.com" }
            .Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    private static HomeCardAvailability Availability(JsonElement node)
    {
        var badges = node.Get("badges").Items().Select(badge => badge.Get("musicInlineBadgeRenderer")
            .Get("accessibilityData").Get("accessibilityData").Get("label").String());
        var markers = string.Join(" ", badges.Prepend(node.Get("musicItemRendererDisplayPolicy").String())
            .Prepend(node.Get("unplayableText").Text())).ToLowerInvariant();
        if (markers.Contains("private", StringComparison.Ordinal)) return HomeCardAvailability.PrivateItem;
        if (markers.Contains("deleted", StringComparison.Ordinal) || markers.Contains("removed", StringComparison.Ordinal))
            return HomeCardAvailability.Deleted;
        if (markers.Contains("region", StringComparison.Ordinal) || markers.Contains("country", StringComparison.Ordinal))
            return HomeCardAvailability.RegionBlocked;
        return markers.Contains("unavailable", StringComparison.Ordinal) || markers.Contains("grey_out", StringComparison.Ordinal)
            ? HomeCardAvailability.Unavailable
            : HomeCardAvailability.Available;
    }

    private static JsonElement FirstProperty(JsonElement node, params string[] names) =>
        names.Select(name => node.Get(name)).FirstOrDefault(value => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null));

    private sealed class ParseAudit
    {
        public int ValidSections { get; set; }
        public int InvalidSections { get; set; }
        public int ValidItems { get; set; }
        public int InvalidItems { get; set; }
    }
}
