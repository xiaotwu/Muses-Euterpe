using Muses.Core.Catalog;
using Muses.Core.Preferences;

namespace Muses.Core.Search;

public enum GlobalSearchScope
{
    All,
    Library,
    YouTube
}

public abstract record GlobalSearchRoute
{
    public sealed record Section(SidebarSection TargetSection) : GlobalSearchRoute;
    public sealed record Release(CatalogReleaseProjection TargetRelease) : GlobalSearchRoute;
    public sealed record Artist(CatalogArtistProjection TargetArtist) : GlobalSearchRoute;
}
