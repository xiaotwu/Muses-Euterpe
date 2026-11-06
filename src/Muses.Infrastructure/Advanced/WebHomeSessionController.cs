using System.Globalization;
using Muses.Core.Advanced;
using Muses.Core.Preferences;

namespace Muses.Infrastructure.Advanced;

public sealed class WebHomeSessionController
{
    public const int ConsentVersion = 2;

    private readonly IWebHomeHelperClient _helperClient;
    private readonly Func<string?> _currentChannelIdProvider;
    private readonly IPreferences _preferences;
    private readonly WebHomeCookieSourceDescriptor _cookieSource = new("edge");
    private readonly object _gate = new();
    private readonly SemaphoreSlim _requests = new(1);
    private CancellationTokenSource? _request;
    private string? _verifiedChannelId;
    private DateTimeOffset? _expiresAt;
    private WebHomeSessionStatus _status = WebHomeSessionStatus.Closed;
    private DateTimeOffset? _lastCheckedAt;
    private bool _isEnabled;
    private bool _hasConsent;

    public string BrowserDisplayName => _cookieSource.BrowserName == "edge" ? "Microsoft Edge" : "Google Chrome";
    public WebHomeSessionStatus Status
    {
        get
        {
            lock (_gate)
                return _status == WebHomeSessionStatus.Available && _expiresAt <= DateTimeOffset.UtcNow
                    ? WebHomeSessionStatus.Expired
                    : _status;
        }
    }

    public DateTimeOffset? LastCheckedAt
    {
        get { lock (_gate) return _lastCheckedAt; }
    }

    public string? VerifiedChannelId
    {
        get
        {
            lock (_gate)
                return IsEnabled && HasConsent && _expiresAt > DateTimeOffset.UtcNow
                    && _verifiedChannelId == _currentChannelIdProvider()
                    ? _verifiedChannelId
                    : null;
        }
    }

    public bool IsEnabled
    {
        get { lock (_gate) return _isEnabled; }
    }

    public bool HasConsent
    {
        get { lock (_gate) return _hasConsent; }
    }

    public event Action? StatusChanged;

    public WebHomeSessionController(
        IWebHomeHelperClient helperClient,
        Func<string?> currentChannelIdProvider,
        IPreferences? preferences = null)
    {
        _helperClient = helperClient;
        _currentChannelIdProvider = currentChannelIdProvider;
        _preferences = preferences ?? new MemoryPreferences();
        _isEnabled = _preferences.GetBool(PrefKey.WebHomeEnabled, false);
        _hasConsent = _preferences.GetString(PrefKey.WebHomeConsentVersion, "0")
            == ConsentVersion.ToString(CultureInfo.InvariantCulture);
        if (IsEnabled && !HasConsent)
            _status = WebHomeSessionStatus.PendingConsent;
    }

    public void EnableWithConsent()
    {
        lock (_gate)
        {
            _preferences.SetString(PrefKey.WebHomeConsentVersion, ConsentVersion.ToString(CultureInfo.InvariantCulture));
            _preferences.SetBool(PrefKey.WebHomeEnabled, true);
            _isEnabled = _hasConsent = true;
            if (_status == WebHomeSessionStatus.PendingConsent)
                _status = WebHomeSessionStatus.Closed;
        }
        StatusChanged?.Invoke();
    }

    public Task DisableAndClearAsync() => ResetAsync(revokeConsent: true);

    public Task ResetAsync() => ResetAsync(revokeConsent: false);

    public Task CancelAsync() => ResetAsync();

    private async Task ResetAsync(bool revokeConsent)
    {
        lock (_gate)
        {
            if (revokeConsent)
            {
                _preferences.SetBool(PrefKey.WebHomeEnabled, false);
                _preferences.SetString(PrefKey.WebHomeConsentVersion, "0");
                _isEnabled = _hasConsent = false;
            }

            _request?.Cancel();
            _request = null;
            ClearVerification();
            _status = IsEnabled && !HasConsent ? WebHomeSessionStatus.PendingConsent : WebHomeSessionStatus.Closed;
        }
        StatusChanged?.Invoke();

        // A canceled operation keeps the serial slot until its helper has exited and cleaned up.
        await _requests.WaitAsync().ConfigureAwait(false);
        _requests.Release();
    }

    public Task ProbeSessionAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("probeSession", WebHomeSessionStatus.Checking, cancellationToken);

    public Task<WebHomeResponse?> FetchHomeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("fetchHome", WebHomeSessionStatus.Refreshing, cancellationToken);

    private async Task<WebHomeResponse?> ExecuteAsync(
        string action,
        WebHomeSessionStatus loading,
        CancellationToken cancellationToken)
    {
        string? channelId;
        CancellationTokenSource? request = null;
        lock (_gate)
        {
            channelId = _currentChannelIdProvider();
            if (!cancellationToken.IsCancellationRequested && !string.IsNullOrWhiteSpace(channelId) && IsEnabled && HasConsent)
            {
                _request?.Cancel();
                request = _request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _status = loading;
            }
        }
        if (request is null)
            return null;
        StatusChanged?.Invoke();

        WebHomeResponse? response;
        try
        {
            await _requests.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                var culture = CultureInfo.CurrentUICulture;
                var locale = culture.Equals(CultureInfo.InvariantCulture) ? "en" : culture.Name;
                var region = culture.IsNeutralCulture || culture.Equals(CultureInfo.InvariantCulture)
                    ? "US"
                    : new RegionInfo(culture.Name).TwoLetterISORegionName;
                var helperRequest = new WebHomeRequest(action, channelId!, _cookieSource, locale, region);
                response = await _helperClient.ExecuteAsync(helperRequest, request.Token).ConfigureAwait(false);
            }
            finally
            {
                _requests.Release();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            response = null;
        }
        catch
        {
            response = WebHomeResponse.Unavailable("helperCrashed");
        }

        WebHomeResponse? verified = null;
        lock (_gate)
        {
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                ClearVerification();
                if (request.IsCancellationRequested || channelId != _currentChannelIdProvider() || !IsEnabled || !HasConsent)
                    _status = WebHomeSessionStatus.Closed;
                else
                {
                    _status = ResolveStatus(response, action, channelId!);
                    if (_status == WebHomeSessionStatus.Available)
                    {
                        _verifiedChannelId = channelId;
                        _lastCheckedAt = response!.FetchedAt ?? DateTimeOffset.UtcNow;
                        _expiresAt = response.ExpiresAt;
                        verified = response;
                    }
                }
            }
            request.Dispose();
        }
        StatusChanged?.Invoke();
        return verified;
    }

    private void ClearVerification()
    {
        _verifiedChannelId = null;
        _lastCheckedAt = null;
        _expiresAt = null;
    }

    private static WebHomeSessionStatus ResolveStatus(WebHomeResponse? response, string action, string channelId)
    {
        if (response is null)
            return WebHomeSessionStatus.Closed;
        if (response.ProtocolVersion != WebHomeProtocolVersion.Current)
            return WebHomeSessionStatus.Unavailable;
        if (response.ChannelId is not null && response.ChannelId != channelId)
            return WebHomeSessionStatus.AccountMismatch;
        return response.ErrorCode switch
        {
            "accountMismatch" => WebHomeSessionStatus.AccountMismatch,
            "sessionExpired" => WebHomeSessionStatus.Expired,
            "shapeChanged" => WebHomeSessionStatus.ShapeChanged,
            "cancelled" => WebHomeSessionStatus.Closed,
            null when response.IsAvailable && response.ChannelId == channelId
                && response.ExpiresAt > DateTimeOffset.UtcNow
                && (action == "probeSession" || response.Sections is { Count: > 0 })
                    => WebHomeSessionStatus.Available,
            _ => WebHomeSessionStatus.Unavailable
        };
    }
}
