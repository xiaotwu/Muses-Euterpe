namespace Muses.App.ViewModels;

public sealed partial class PodcastsViewModel
{
    public PodcastLabels Labels { get; private set; } = new();

    public bool HasFollowedShows => FollowedShows.Count > 0;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasContinueEpisodes => ContinueEpisodes.Count > 0;
    public bool NoSearchResults => SearchCompleted && !IsSearching && !SearchFailed && !HasSearchResults;
    public bool NoEpisodes => !IsLoadingShow && !ShowLoadFailed && Episodes.Count == 0;
    public string FollowActionLabel => PodcastLabels.FollowAction(IsFollowing);
    public string UnplayedOrderLabel => PodcastLabels.UnplayedOrder(UnplayedUsesPublicationOrder);

    public void RefreshLabels()
    {
        Labels = new();
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(FollowActionLabel));
        OnPropertyChanged(nameof(UnplayedOrderLabel));
        foreach (var row in Episodes.Concat(ContinueEpisodes))
            row.RefreshLabels();
    }
}

public sealed partial class PodcastEpisodeRow
{
    public string StatusLabel => !IsAvailable ? PodcastLabels.Unavailable
        : IsCompleted ? PodcastLabels.Played
        : CanResume ? PodcastLabels.ResumeAt(FormatTime(PositionSeconds))
        : PodcastLabels.Unplayed;

    public string PlaybackLabel => PodcastLabels.PlayEpisode(Title, CanResume);
    public string MarkActionLabel => PodcastLabels.MarkEpisode(Title, IsCompleted);

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}
