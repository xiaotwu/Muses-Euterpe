using System.Text.Json;

namespace Muses.WebHome;

internal static class WebHomeIdentity
{
    public static string? ActiveChannel(JsonElement response)
    {
        var channels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in response.Descendants())
        {
            AddExplicitChannels(node.Get("activeAccountHeaderRenderer"), channels);
            var account = node.Get("accountItemRenderer");
            if (account.Get("isSelected").ValueKind == JsonValueKind.True)
                AddExplicitChannels(account, channels);

            var menu = node.Get("multiPageMenuRenderer");
            if (menu.Get("header").Get("activeAccountHeaderRenderer").ValueKind != JsonValueKind.Object)
                continue;
            foreach (var section in menu.Get("sections").Items())
            foreach (var item in section.Get("multiPageMenuSectionRenderer").Get("items").Items())
                AddChannel(item.Get("compactLinkRenderer").Get("navigationEndpoint").Get("browseEndpoint")
                    .Get("browseId").String(), channels);
        }

        if (channels.Count > 1)
            throw new WebHomeFailure("accountMismatch");
        return channels.SingleOrDefault();
    }

    public static string SelectedHandle(JsonElement response)
    {
        var selected = response.Descendants()
            .Select(node => node.Get("accountItem"))
            .Where(account => account.Get("isSelected").ValueKind == JsonValueKind.True)
            .Take(2).ToArray();
        var handle = selected.Length == 1 ? selected[0].Get("channelHandle").Text() : null;
        if (handle is null || !handle.StartsWith('@') || handle.Length is < 2 or > 100
            || handle.Skip(1).Any(c => !char.IsLetterOrDigit(c) && c is not '.' and not '_' and not '-' and not '·'))
            throw new WebHomeFailure("identityUnavailable");
        return handle;
    }

    public static string ResolvedChannel(JsonElement response)
    {
        var id = response.Get("endpoint").Get("browseEndpoint").Get("browseId").String();
        if (!IsChannelId(id))
            throw new WebHomeFailure("identityUnavailable");
        return id!;
    }

    private static void AddExplicitChannels(JsonElement node, HashSet<string> channels)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        foreach (var key in new[] { "channelId", "channelID", "browseId" })
            AddChannel(node.Get(key).String(), channels);
        AddChannel(node.Get("browseEndpoint").Get("browseId").String(), channels);
        foreach (var key in new[] { "endpoint", "serviceEndpoint", "navigationEndpoint" })
            AddExplicitChannels(node.Get(key), channels);
    }

    private static void AddChannel(string? id, HashSet<string> channels)
    {
        if (IsChannelId(id))
            channels.Add(id!);
    }

    private static bool IsChannelId(string? id) =>
        id?.StartsWith("UC", StringComparison.Ordinal) == true && WebHomeJson.IsId(id, 20, 32);
}
