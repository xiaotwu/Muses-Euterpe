using System.Text.Json;
using Muses.Core.Advanced;
using Muses.WebHome;

namespace Muses.WebHome.Helper;

public static class Program
{
    private const int MaximumRequestBytes = 64 * 1024;
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
            var input = await ReadRequestAsync(stdin).ConfigureAwait(false);
            var request = input is null ? null : JsonSerializer.Deserialize<WebHomeRequest>(input, JsonOptions);
            if (request?.WorkspaceId is not Guid workspaceId || workspaceId == Guid.Empty)
            {
                response = WebHomeResponse.Unavailable("malformedResponse");
            }
            else
            {
                using var cancellation = new CancellationTokenSource();
                _ = Task.Run(() => CancelOnInputClosedAsync(stdin, cancellation));
                response = await WebHomeProbeCommand.ExecuteAsync(request, cancellationToken: cancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (JsonException)
        {
            response = WebHomeResponse.Unavailable("malformedResponse");
        }
        catch (OperationCanceledException)
        {
            response = WebHomeResponse.Unavailable("cancelled");
        }
        catch (Exception ex)
        {
            response = WebHomeResponse.Unavailable("helperCrashed", ex.GetType().Name);
        }

        var output = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        await Console.OpenStandardOutput().WriteAsync(output).ConfigureAwait(false);
        return 0;
    }

    private static async Task<byte[]?> ReadRequestAsync(Stream stdin)
    {
        using var request = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stdin.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) return null;

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
            var length = newline >= 0 ? newline : count;
            if (request.Length + length > MaximumRequestBytes) return null;
            request.Write(buffer, 0, length);
            if (newline >= 0)
                return newline == count - 1 && request.Length > 0 ? request.ToArray() : null;
        }
    }

    private static async Task CancelOnInputClosedAsync(Stream stdin, CancellationTokenSource cancellation)
    {
        try
        {
            // The caller keeps stdin open during the request and closes it to cancel.
            await stdin.ReadAsync(new byte[1]).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { return; }

        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
