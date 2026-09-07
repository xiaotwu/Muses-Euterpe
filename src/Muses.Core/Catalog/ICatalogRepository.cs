namespace Muses.Core.Catalog;

public sealed class CatalogReleaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CatalogId { get; set; } = "";
    public string StableId { get => CatalogId; set => CatalogId = value; }
    public string Title { get; set; } = "";
    public string? ArtistName { get; set; }
    public string? KindRaw { get; set; }
    public CatalogReleaseKind Kind
    {
        get => Enum.TryParse<CatalogReleaseKind>(KindRaw, true, out var k) ? k : CatalogReleaseKind.Unknown;
        set => KindRaw = value.ToString();
    }
    public string? ArtworkUrl { get; set; }
    public int? Year { get; set; }
    public string? ArtistCatalogId { get; set; }
    public string? ArtistStableId { get => ArtistCatalogId; set => ArtistCatalogId = value; }
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Unavailable { get; set; }
}

public sealed class CatalogArtistEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CatalogId { get; set; } = "";
    public string StableId { get => CatalogId; set => CatalogId = value; }
    public string Name { get; set; } = "";
    public string? ArtworkUrl { get; set; }
    public string? ChannelId { get; set; }
    public string? BrowseId { get; set; }
    public string? Biography { get; set; }
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Unavailable { get; set; }
}

public interface ICatalogRepository
{
    void UpsertRelease(CatalogReleaseEntity release);
    void UpsertArtist(CatalogArtistEntity artist);
    IReadOnlyList<CatalogReleaseEntity> GetReleases();
    IReadOnlyList<CatalogArtistEntity> GetArtists();
    CatalogReleaseEntity? GetRelease(string catalogId);
    CatalogArtistEntity? GetArtist(string catalogId);
    void DeleteOrphanReleases(ISet<string> activeCatalogIds);
    void DeleteOrphanArtists(ISet<string> activeCatalogIds);
}
