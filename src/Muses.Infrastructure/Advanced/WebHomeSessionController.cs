using Muses.Core.Advanced;

namespace Muses.Infrastructure.Advanced;

public sealed class WebHomeSessionController
{
    private readonly IWebHomeHelperClient _helperClient;
    private readonly Func<string?> _currentChannelIdProvider;

    public WebHomeSessionStatus Status { get; private set; } = WebHomeSessionStatus.Closed;
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public bool IsEnabled { get; set; }
    public bool HasConsent { get; set; }

    public event Action? StatusChanged;

    public WebHomeSessionController(
        IWebHomeHelperClient helperClient,
        Func<string?> currentChannelIdProvider)
    {
        _helperClient = helperClient;
        _currentChannelIdProvider = currentChannelIdProvider;
    }

    public async Task ProbeSessionAsync(CancellationToken cancellationToken = default)
    {
        var channelId = _currentChannelIdProvider();
        if (string.IsNullOrEmpty(channelId))
        {
            Status = WebHomeSessionStatus.Closed;
            StatusChanged?.Invoke();
            return;
        }

        Status = WebHomeSessionStatus.Checking;
        StatusChanged?.Invoke();

        try
        {
            var req = new WebHomeRequest("probeSession", channelId, new WebHomeCookieSourceDescriptor("Default"), "en", "US");
            var res = await _helperClient.ExecuteAsync(req, cancellationToken);
            if (res.IsAvailable && res.ChannelId == channelId)
            {
                Status = WebHomeSessionStatus.Available;
                LastCheckedAt = res.FetchedAt ?? DateTimeOffset.UtcNow;
            }
            else if (res.ChannelId != null && res.ChannelId != channelId)
            {
                Status = WebHomeSessionStatus.AccountMismatch;
            }
            else
            {
                Status = WebHomeSessionStatus.Unavailable;
            }
        }
        catch (OperationCanceledException)
        {
            Status = WebHomeSessionStatus.Closed;
        }
        catch
        {
            Status = WebHomeSessionStatus.Unavailable;
        }

        StatusChanged?.Invoke();
    }
}
