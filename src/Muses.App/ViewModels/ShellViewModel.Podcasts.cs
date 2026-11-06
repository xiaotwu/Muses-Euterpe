using Muses.Core.Preferences;

namespace Muses.App.ViewModels;

public partial class ShellViewModel
{
    public PodcastsViewModel? Podcasts { get; internal set; }
    public bool IsPodcasts => SelectedSection == SidebarSection.Podcasts;
    public string PodcastsLabel => PodcastLabels.Title;
}
