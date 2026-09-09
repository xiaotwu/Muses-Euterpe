using Muses.Core.Advanced;
using Muses.Core.Preferences;

namespace Muses.Infrastructure.Advanced;

public sealed class WebHomeSessionController
{
    public const int ConsentVersion = 1;

    private readonly IWebHomeHelperClient _helperClient;
    private readonly Func<string?> _currentChannelIdProvider;
    private readonly IPreferences _preferences;
    private CancellationTokenSource? _probeCts;

    public WebHomeSessionStatus Status { get; private set; } = WebHomeSessionStatus.Closed;
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public bool IsEnabled
    {
        get => _preferences.GetBool(PrefKey.WebHomeEnabled, false);
        set => _preferences.SetBool(PrefKey.WebHomeEnabled, value);
    }

    public bool HasConsent
    {
        get
        {
            var raw = _preferences.GetString(PrefKey.WebHomeConsentVersion, "0");
            return int.TryParse(raw, out var v) && v >= ConsentVersion;
        }
    }

    public event Action? StatusChanged;

    public WebHomeSessionController(
        IWebHomeHelperClient helperClient,
        Func<string?> currentChannelIdProvider,
        IPreferences? preferences = null)
    {
        _helperClient = helperClient;
        _currentChannelIdProvider = currentChannelIdProvider;
        _preferences = preferences ?? MemoryPreferences.Empty;
        SyncInitialStatus();
    }

    private void SyncInitialStatus()
    {
        if (!IsEnabled)
            Status = WebHomeSessionStatus.Closed;
        else if (!HasConsent)
            Status = WebHomeSessionStatus.PendingConsent;
        else
            Status = WebHomeSessionStatus.Closed;
    }

    public void GrantConsent()
    {
        _preferences.SetString(PrefKey.WebHomeConsentVersion, ConsentVersion.ToString());
        if (IsEnabled)
            Status = WebHomeSessionStatus.Closed;
        else
            Status = WebHomeSessionStatus.PendingConsent;
        StatusChanged?.Invoke();
    }

    public void EnableWithConsent()
    {
        GrantConsent();
        IsEnabled = true;
        Status = WebHomeSessionStatus.Closed;
        StatusChanged?.Invoke();
    }

    public async Task DisableAndClearAsync()
    {
        IsEnabled = false;
        _preferences.SetString(PrefKey.WebHomeConsentVersion, "0");
        await CancelAsync().ConfigureAwait(false);
        Status = WebHomeSessionStatus.Closed;
        StatusChanged?.Invoke();
    }

    public async Task CancelAsync()
    {
        try { _probeCts?.Cancel(); } catch { /* ignore */ }
        await _helperClient.CancelAsync().ConfigureAwait(false);
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

        if (!IsEnabled)
        {
            Status = WebHomeSessionStatus.Closed;
            StatusChanged?.Invoke();
            return;
        }

        if (!HasConsent)
        {
            Status = WebHomeSessionStatus.PendingConsent;
            StatusChanged?.Invoke();
            return;
        }

        _probeCts?.Cancel();
        _probeCts?.Dispose();
        _probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _probeCts.Token;

        Status = WebHomeSessionStatus.Checking;
        StatusChanged?.Invoke();

        try
        {
            var req = new WebHomeRequest(
                "probeSession",
                channelId,
                new WebHomeCookieSourceDescriptor("Default"),
                "en",
                "US");
            var res = await _helperClient.ExecuteAsync(req, ct).ConfigureAwait(false);

            if (string.Equals(res.ErrorCode, "accountMismatch", StringComparison.OrdinalIgnoreCase)
                || (res.ChannelId != null && res.ChannelId != channelId))
            {
                Status = WebHomeSessionStatus.AccountMismatch;
            }
            else if (res.IsAvailable && res.ChannelId == channelId)
            {
                Status = WebHomeSessionStatus.Available;
                LastCheckedAt = res.FetchedAt ?? DateTimeOffset.UtcNow;
            }
            else if (string.Equals(res.ErrorCode, "shapeChanged", StringComparison.OrdinalIgnoreCase))
            {
                Status = WebHomeSessionStatus.ShapeChanged;
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
