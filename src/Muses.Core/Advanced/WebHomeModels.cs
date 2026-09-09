namespace Muses.Core.Advanced;

public enum WebHomeSessionStatus
{
    Closed,
    DisabledByBuild,
    PendingConsent,
    Checking,
    Refreshing,
    Available,
    Expired,
    AccountMismatch,
    ShapeChanged,
    Unavailable
}

public static class WebHomeProtocolVersion
{
    public const int Current = 1;
}

public sealed record WebHomeCookieSourceDescriptor(
    string BrowserName,
    string? BrowserProfile = null,
    string? FilePath = null)
{
    public bool IsFileSource => !string.IsNullOrWhiteSpace(FilePath);
}

public sealed record WebHomeRequest(
    string Action,
    string ExpectedChannelId,
    WebHomeCookieSourceDescriptor CookieSource,
    string Locale,
    string Region,
    int TimeoutMs = 12000,
    string? ContinuationToken = null,
    int ProtocolVersion = WebHomeProtocolVersion.Current);

public sealed record WebHomeResponse(
    string? ChannelId,
    bool IsAvailable,
    DateTimeOffset? FetchedAt,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode,
    string? ErrorMessage,
    int ProtocolVersion = WebHomeProtocolVersion.Current)
{
    public static WebHomeResponse Available(string channelId, DateTimeOffset? fetchedAt = null) =>
        new(channelId, true, fetchedAt ?? DateTimeOffset.UtcNow,
            (fetchedAt ?? DateTimeOffset.UtcNow).AddMinutes(15), null, null);

    public static WebHomeResponse Unavailable(string code, string? message = null) =>
        new(null, false, null, null, code, message);

    public static WebHomeResponse Mismatch(string channelId) =>
        new(channelId, false, DateTimeOffset.UtcNow, null, "accountMismatch",
            "Cookie session channel does not match the signed-in account.");
}

public interface IWebHomeHelperClient
{
    Task<WebHomeResponse> ExecuteAsync(WebHomeRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync();
}

/// <summary>In-process fake used by tests. Never touches cookies or network.</summary>
public sealed class FakeWebHomeHelperClient : IWebHomeHelperClient
{
    private readonly Func<WebHomeRequest, WebHomeResponse> _handler;
    public int ExecuteCount { get; private set; }
    public int CancelCount { get; private set; }
    public WebHomeRequest? LastRequest { get; private set; }

    public FakeWebHomeHelperClient(Func<WebHomeRequest, WebHomeResponse>? handler = null)
    {
        _handler = handler ?? (_ => WebHomeResponse.Unavailable("offline"));
    }

    public FakeWebHomeHelperClient(WebHomeResponse fixedResponse)
        : this(_ => fixedResponse)
    {
    }

    public Task<WebHomeResponse> ExecuteAsync(WebHomeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecuteCount++;
        LastRequest = request;
        return Task.FromResult(_handler(request));
    }

    public Task CancelAsync()
    {
        CancelCount++;
        return Task.CompletedTask;
    }
}
