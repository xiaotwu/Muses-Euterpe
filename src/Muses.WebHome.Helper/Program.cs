using System.Text;
using System.Text.Json;
using Muses.Core.Advanced;
using Muses.WebHome;

namespace Muses.WebHome.Helper;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> Main(string[] args)
    {
        WebHomeResponse response;
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var ms = new MemoryStream();
            await stdin.CopyToAsync(ms).ConfigureAwait(false);
            if (ms.Length == 0 || ms.Length > 64 * 1024)
            {
                response = WebHomeResponse.Unavailable("malformedResponse", "Request body missing or too large.");
            }
            else
            {
                var request = JsonSerializer.Deserialize<WebHomeRequest>(ms.ToArray(), JsonOptions)
                              ?? throw new InvalidOperationException("null request");
                response = await WebHomeProbeCommand.ExecuteAsync(request).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            response = WebHomeResponse.Unavailable("helperCrashed", ex.GetType().Name);
        }

        var output = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        await Console.OpenStandardOutput().WriteAsync(output).ConfigureAwait(false);
        return 0;
    }
}
