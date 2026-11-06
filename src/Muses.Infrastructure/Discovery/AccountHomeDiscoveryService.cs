using Muses.Core.Discovery;
using Muses.Infrastructure.Advanced;

namespace Muses.Infrastructure.Discovery;

public sealed class AccountHomeDiscoveryService(WebHomeSessionController session)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _refresh;
    private HomeSnapshot? _snapshot;
    private Timer? _expiry;

    public event Action? Changed;

    public bool IsRefreshing
    {
        get { lock (_gate) return _refresh is not null; }
    }

    public IReadOnlyList<HomeSection> Sections
    {
        get
        {
            HomeSnapshot? snapshot;
            lock (_gate) snapshot = _snapshot;
            return MatchesChannel(snapshot, session.VerifiedChannelId) ? snapshot!.Sections : [];
        }
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        var verifiedChannel = session.VerifiedChannelId;
        CancellationTokenSource refresh;
        lock (_gate)
        {
            if (_refresh is not null || (!forceReload && MatchesChannel(_snapshot, verifiedChannel))) return;
            _refresh = refresh = new CancellationTokenSource();
            ClearSnapshot();
        }

        try
        {
            Changed?.Invoke();
            var response = await session.FetchHomeAsync(refresh.Token).ConfigureAwait(false);
            if (response is not { ChannelId: { } channelId, FetchedAt: { } fetchedAt,
                ExpiresAt: { } expiresAt, Sections: { Count: > 0 } shelves }) return;

            var sections = shelves.Select(shelf => new HomeSection(
                shelf.Id, shelf.Title, shelf.Subtitle, shelf.Kind,
                shelf.Items.Select(item => (DiscoveryItem)new DiscoveryItem.YouTube(new YouTubeDiscoveryCard(
                    item.Identity.Identifier, item.Title, item.Subtitle, thumbnailUrl: item.ArtworkUrl,
                    browseEndpoint: item.BrowseEndpoint ?? (item.Identity.Kind == HomeCardEndpointKind.Video ? null : item.Identity),
                    playEndpoint: item.PlayEndpoint, availability: item.Availability))).ToArray(),
                source: HomeSource.SignedInWeb, fetchedAt: fetchedAt)).ToArray();

            lock (_gate)
            {
                var remaining = expiresAt - DateTimeOffset.UtcNow;
                if (_refresh == refresh && remaining > TimeSpan.Zero)
                {
                    var snapshot = new HomeSnapshot(new HomeFeedScope.Account(channelId), sections, fetchedAt, expiresAt);
                    _snapshot = snapshot;
                    var lifetime = remaining < HomeFeedCache.WebFreshWindow ? remaining : HomeFeedCache.WebFreshWindow;
                    _expiry = new Timer(_ => Expire(snapshot), null, lifetime, Timeout.InfiniteTimeSpan);
                }
            }
        }
        finally
        {
            bool wasActive;
            lock (_gate)
            {
                wasActive = _refresh == refresh;
                if (wasActive) _refresh = null;
                refresh.Dispose();
            }
            if (wasActive) Changed?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            var refresh = _refresh;
            _refresh = null;
            ClearSnapshot();
            refresh?.Cancel();
        }
        Changed?.Invoke();
    }

    private void Expire(HomeSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_snapshot != snapshot) return;
            ClearSnapshot();
        }
        Changed?.Invoke();
    }

    private void ClearSnapshot()
    {
        _snapshot = null;
        _expiry?.Dispose();
        _expiry = null;
    }

    private static bool MatchesChannel(HomeSnapshot? snapshot, string? verifiedChannel) =>
        snapshot is { Scope: HomeFeedScope.Account account } && account.ChannelId == verifiedChannel;
}
