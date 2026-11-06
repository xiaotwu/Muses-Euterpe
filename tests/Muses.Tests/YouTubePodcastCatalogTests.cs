using System.Net;
using System.Text.Json;
using Muses.Infrastructure.Discovery;

namespace Muses.Tests;

public sealed class YouTubePodcastCatalogTests
{
    [Fact]
    public async Task SearchUsesSourceFilterAndReturnsOnlyTypedPodcastShows()
    {
        var transport = new CatalogTransport("""
            {"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{"content":{"sectionListRenderer":{
              "header":{"chipCloudRenderer":{"chips":[{"chipCloudChipRenderer":{
                "text":{"runs":[{"text":"Podcasts"}]},
                "navigationEndpoint":{"searchEndpoint":{"params":"source-token"}}
              }}]}}
            }}}}]}}}
            """, """
            {"contents":{"musicShelfRenderer":{"contents":[
              {"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{
                "text":{"runs":[{"text":"A show","navigationEndpoint":{"browseEndpoint":{
                  "browseId":"MPSP_show","browseEndpointContextSupportedConfigs":{
                    "browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE"}
                  }
                }}}]}
              }}],"thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://example.com/show.jpg"}]}}}}},
              {"musicTwoRowItemRenderer":{"title":{"simpleText":"A playlist"},"navigationEndpoint":{"browseEndpoint":{
                "browseId":"PL_list","browseEndpointContextSupportedConfigs":{
                  "browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_PLAYLIST"}
                }
              }}}}
            ]}}}
            """);
        using var client = new HttpClient(transport);
        var catalog = new YouTubePodcastCatalog(client);

        var shows = await catalog.SearchAsync("A show", "en", "US", CancellationToken.None);

        Assert.Equal(new PodcastShowCard("browse:MPSP_show", "A show", "https://example.com/show.jpg"), Assert.Single(shows));
        Assert.Equal("source-token", transport.Requests.Last().GetProperty("params").GetString());
        Assert.Equal("1.20261104.00.00", transport.Requests.First().GetProperty("context")
            .GetProperty("client").GetProperty("clientVersion").GetString());
    }

    [Fact]
    public async Task BrowseFollowsSourceCursorAndExcludesRelatedShelves()
    {
        var transport = new CatalogTransport("""
            {"contents":{"sectionListRenderer":{"contents":[
              {"musicShelfRenderer":{"contents":[
                {"musicMultiRowListItemRenderer":{"title":{"simpleText":"First"},"onTap":{"watchEndpoint":{
                  "videoId":"abcdefghijk","watchEndpointMusicSupportedConfigs":{"watchEndpointMusicConfig":{
                    "musicVideoType":"MUSIC_VIDEO_TYPE_PODCAST_EPISODE"
                  }}
                }}}},
                {"continuationItemRenderer":{"continuationEndpoint":{"continuationCommand":{"token":"cursor-1"}}}}
              ]}},
              {"musicCarouselShelfRenderer":{"contents":[
                {"musicMultiRowListItemRenderer":{"title":{"simpleText":"Related"},"onTap":{"watchEndpoint":{
                  "videoId":"zzzzzzzzzzz","watchEndpointMusicSupportedConfigs":{"watchEndpointMusicConfig":{
                    "musicVideoType":"MUSIC_VIDEO_TYPE_PODCAST_EPISODE"
                  }}
                }}}}
              ]}}
            ]}}}
            """, """
            {"onResponseReceivedActions":[{"appendContinuationItemsAction":{"continuationItems":[
              {"musicMultiRowListItemRenderer":{"title":{"simpleText":"Second"},"onTap":{"watchEndpoint":{
                "videoId":"ABCDEFGHIJK","watchEndpointMusicSupportedConfigs":{"watchEndpointMusicConfig":{
                  "musicVideoType":"MUSIC_VIDEO_TYPE_PODCAST_EPISODE"
                }}
              }}}}
            ]}}]}
            """);
        using var client = new HttpClient(transport);
        var catalog = new YouTubePodcastCatalog(client);
        var ct = CancellationToken.None;

        var first = await catalog.BrowseAsync("browse:MPSP_show", null, "en", "US", ct);
        var next = await catalog.BrowseAsync("browse:MPSP_show", first.Next, "en", "US", ct);

        Assert.Equal("abcdefghijk", Assert.Single(first.Episodes).VideoId);
        Assert.Equal("ABCDEFGHIJK", Assert.Single(next.Episodes).VideoId);
        Assert.Null(next.Next);
        Assert.Equal("MPSP_show", transport.Requests[0].GetProperty("browseId").GetString());
        Assert.Equal("cursor-1", transport.Requests[1].GetProperty("continuation").GetString());
    }

    private sealed class CatalogTransport(params string[] pages) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"INNERTUBE_CLIENT_VERSION":"1.20261104.00.00"}""")
                };

            Assert.Equal("https://music.youtube.com", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.False(request.Headers.Contains("Cookie"));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(body.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[Requests.Count - 1])
            };
        }
    }
}
