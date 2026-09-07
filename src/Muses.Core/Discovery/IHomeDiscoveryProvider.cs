namespace Muses.Core.Discovery;

public sealed record HomeFetchFailure(string Layer, string Code, string? Message);

public sealed class HomeFetchResult
{
    public HomeSnapshot BaselineSnapshot { get; }
    public HomeSnapshot? WebSnapshot { get; }
    public IReadOnlyList<HomeFetchFailure> Failures { get; }
    public bool StoreBaseline { get; }
    public bool StoreWeb { get; }

    public HomeFetchResult(
        HomeSnapshot baselineSnapshot,
        HomeSnapshot? webSnapshot = null,
        IReadOnlyList<HomeFetchFailure>? failures = null,
        bool storeBaseline = true,
        bool storeWeb = false)
    {
        BaselineSnapshot = baselineSnapshot;
        WebSnapshot = webSnapshot;
        Failures = failures ?? Array.Empty<HomeFetchFailure>();
        StoreBaseline = storeBaseline;
        StoreWeb = storeWeb;
    }

    public static HomeFetchResult Baseline(
        HomeFeedScope scope,
        IReadOnlyList<HomeSection> sections,
        DateTimeOffset? fetchedAt = null,
        IReadOnlyList<HomeFetchFailure>? failures = null)
    {
        var now = fetchedAt ?? DateTimeOffset.UtcNow;
        var hasSectionFailure = sections.Any(s => s.Status is SectionStatus.Failed);
        var fails = failures ?? Array.Empty<HomeFetchFailure>();
        var snapshot = new HomeSnapshot(
            scope,
            sections,
            now,
            now.Add(TimeSpan.FromMinutes(30)));

        return new HomeFetchResult(
            snapshot,
            null,
            fails,
            storeBaseline: !hasSectionFailure && fails.Count == 0,
            storeWeb: false);
    }
}

public interface IHomeDiscoveryProvider
{
    bool HasWebEnhancement { get; }
    Task<HomeFetchResult> FetchAsync(HomeDiscoveryInput input, CancellationToken ct = default);
    Task<IReadOnlyList<HomeSection>> MoreAsync(int page, HomeDiscoveryInput input, CancellationToken ct = default);
}
