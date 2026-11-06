namespace Muses.Infrastructure.YTDlp;

public sealed class ResolvedAudioStream(
    Uri url, IReadOnlyDictionary<string, string> requestHeaders, DateTimeOffset? expiresAt = null)
{
    internal static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    public Uri Url { get; } = url;
    public IReadOnlyDictionary<string, string> RequestHeaders { get; } = requestHeaders;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;

    internal bool IsExpiring(DateTimeOffset now) => ExpiresAt <= now + ExpiryMargin;
}
