using System.Text.Json;

namespace Muses.WebHome;

internal static class WebHomeJson
{
    public static JsonElement Get(this JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;

    public static string? String(this JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static IEnumerable<JsonElement> Items(this JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    public static IEnumerable<JsonElement> Descendants(this JsonElement value)
    {
        yield return value;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            foreach (var node in Descendants(property.Value))
                yield return node;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            foreach (var node in Descendants(item))
                yield return node;
        }
    }

    public static string? Text(this JsonElement value)
    {
        var text = value.Get("simpleText").String()
            ?? string.Concat(value.Get("runs").Items().Select(run => run.Get("text").String()));
        text = text.Trim();
        return text.Length is > 0 and <= 500 && !text.Contains('\0') ? text : null;
    }

    public static bool IsId(string? value, int minimumLength = 1, int maximumLength = 256) =>
        value is not null && value.Length >= minimumLength && value.Length <= maximumLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal sealed class WebHomeFailure(string code) : Exception
{
    public string Code { get; } = code;
}
