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

public sealed record WebHomeCookieSourceDescriptor(string BrowserName);

public sealed record WebHomeRequest(
    string Action,
    string ExpectedChannelId,
    WebHomeCookieSourceDescriptor CookieSource,
    string Locale,
    string Region,
    int TimeoutMs = 12000,
    string? ContinuationToken = null);

public sealed record WebHomeResponse(
    string? ChannelId,
    bool IsAvailable,
    DateTimeOffset? FetchedAt,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode,
    string? ErrorMessage);

public interface IWebHomeHelperClient
{
    Task<WebHomeResponse> ExecuteAsync(WebHomeRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync();
}
