using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;
using Muses.Core.Playback;
using Muses.Core.Podcasts;
using Muses.Core.YouTube;
using Muses.Infrastructure.Discovery;

namespace Muses.App.ViewModels;

public sealed partial class PodcastEpisodeRow(PodcastEpisodeState state, string showTitle, string? showArtwork) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ArtworkUrl), nameof(PositionSeconds), nameof(DurationSeconds))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent), nameof(IsCompleted), nameof(CanResume), nameof(IsAvailable))]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(PlaybackLabel), nameof(MarkActionLabel))]
    private PodcastEpisodeState _state = state;

    public string ShowTitle { get; } = showTitle;
    public string? ShowArtwork { get; } = showArtwork;
    public string VideoId => State.Episode.VideoId;
    public string Title => State.Episode.Title;
    public string ArtworkUrl => State.Episode.ArtworkUrl ?? ShowArtwork ?? YouTubeLinks.ThumbnailUrl(VideoId);
    public double PositionSeconds => State.LastPositionMs / 1000;
    public double DurationSeconds => State.Episode.DurationMs.GetValueOrDefault() / 1000.0;
    public double ProgressPercent => DurationSeconds > 0 ? Math.Clamp(100 * PositionSeconds / DurationSeconds, 0, 100) : 0;
    public bool IsCompleted => State.Completed;
    public bool CanResume => IsAvailable && !IsCompleted && PositionSeconds > 0;
    public bool IsAvailable => State.Episode.Availability == "available";

    internal void RefreshLabels()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(PlaybackLabel));
        OnPropertyChanged(nameof(MarkActionLabel));
    }
}

public sealed partial class PodcastsViewModel : ObservableObject
{
    private readonly IPodcastRepository _repository;
    private readonly YouTubePodcastCatalog _catalog;
    private readonly LibraryService _library;
    private readonly PlaybackService _playback;
    private readonly PodcastProgressService _progress;
    private readonly List<string> _visibleEpisodeIds = [];
    private CancellationTokenSource? _showRequest;
    private string? _nextEpisodePage;
    private bool _active;
    private bool _operationSaveFailed;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasQuery))] private string _query = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFollowedShows))]
    private IReadOnlyList<PodcastShowCard> _followedShows = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSearchResults), nameof(NoSearchResults))]
    private IReadOnlyList<PodcastShowCard> _searchResults = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasContinueEpisodes))]
    private IReadOnlyList<PodcastEpisodeRow> _continueEpisodes = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoEpisodes), nameof(CanPlayUnplayed), nameof(UnplayedUsesPublicationOrder), nameof(UnplayedOrderLabel))]
    private IReadOnlyList<PodcastEpisodeRow> _episodes = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDetail))]
    private PodcastShowCard? _selectedShow;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FollowActionLabel))] private bool _isFollowing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoSearchResults))] private bool _isSearching;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoSearchResults))] private bool _searchCompleted;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoSearchResults))] private bool _searchFailed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoEpisodes))] private bool _isLoadingShow;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoEpisodes))] private bool _showLoadFailed;
    [ObservableProperty] private bool _hasMoreEpisodes;
    [ObservableProperty] private bool _playFailed;

    public bool SaveFailed => _operationSaveFailed || _progress.PersistenceFailed;
    public bool HasQuery => Query.Length > 0;
    public bool IsDetail => SelectedShow is not null;
    public bool CanPlayUnplayed => Episodes.Any(episode => episode.IsAvailable && !episode.IsCompleted);
    public bool UnplayedUsesPublicationOrder => CanPlayUnplayed &&
        Episodes.Where(episode => episode.IsAvailable && !episode.IsCompleted)
            .All(episode => episode.State.Episode.PublishedAt.HasValue);

    public PodcastsViewModel(IPodcastRepository repository, YouTubePodcastCatalog catalog, LibraryService library,
        PlaybackService playback, PodcastProgressService progress)
    {
        _repository = repository;
        _catalog = catalog;
        _library = library;
        _playback = playback;
        _progress = progress;
        _progress.Changed += OnProgressChanged;
    }

    internal void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!_active) return;
        RefreshLabels();
        ReadLocalState();
    }

    internal void Stop()
    {
        _progress.Changed -= OnProgressChanged;
        _active = false;
        SearchCommand.Cancel();
        _showRequest?.Cancel();
    }

    private void OnProgressChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_active) ReadLocalState();
    });

    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct)
    {
        var query = Query.Trim();
        SearchResults = [];
        SearchCompleted = false;
        SearchFailed = false;
        if (query.Length == 0) return;

        IsSearching = true;
        try
        {
            var (language, region) = Locale();
            var results = await _catalog.SearchAsync(query, language, region, ct);
            ct.ThrowIfCancellationRequested();
            SearchResults = results;
            SearchCompleted = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { SearchFailed = true; }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchCommand.Cancel();
        Query = "";
        SearchResults = [];
        SearchCompleted = false;
        SearchFailed = false;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenShowAsync(PodcastShowCard? show)
    {
        if (show is null || SelectedShow?.CatalogId == show.CatalogId) return;
        SelectedShow = show;
        _visibleEpisodeIds.Clear();
        _nextEpisodePage = null;
        HasMoreEpisodes = false;
        ReadLocalState();
        await LoadShowAsync(more: false);
    }

    [RelayCommand]
    private void BackToShows()
    {
        _showRequest?.Cancel();
        SelectedShow = null;
        Episodes = [];
        HasMoreEpisodes = false;
        ShowLoadFailed = false;
        PlayFailed = false;
    }

    [RelayCommand]
    private Task RefreshShowAsync() => LoadShowAsync(more: false);

    [RelayCommand]
    private Task LoadMoreEpisodesAsync() => LoadShowAsync(more: true);

    private async Task LoadShowAsync(bool more)
    {
        var show = SelectedShow;
        var cursor = more ? _nextEpisodePage : null;
        if (show is null || (more && (cursor is null || IsLoadingMore || IsLoadingShow))) return;

        _showRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _showRequest = request;
        IsLoadingShow = !more;
        IsLoadingMore = more;
        ShowLoadFailed = false;
        PlayFailed = false;
        try
        {
            var (language, region) = Locale();
            var page = await _catalog.BrowseAsync(show.CatalogId, cursor, language, region, request.Token);
            request.Token.ThrowIfCancellationRequested();
            var sourceOffset = more ? _visibleEpisodeIds.Count : 0;
            var seen = new HashSet<string>(more ? _visibleEpisodeIds : [], StringComparer.Ordinal);
            var episodes = page.Episodes.Where(episode => seen.Add(episode.VideoId)).ToArray();
            if (!Save(() => _repository.SaveEpisodes(show.CatalogId, episodes, sourceOffset))) return;

            if (!more) _visibleEpisodeIds.Clear();
            _visibleEpisodeIds.AddRange(episodes.Select(episode => episode.VideoId));
            _nextEpisodePage = page.Next == cursor ? null : page.Next;
            HasMoreEpisodes = _nextEpisodePage is not null;
            ReadLocalState();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { ShowLoadFailed = true; }
        finally
        {
            if (ReferenceEquals(_showRequest, request))
            {
                _showRequest = null;
                IsLoadingShow = false;
                IsLoadingMore = false;
            }
        }
    }

    [RelayCommand]
    private void ToggleFollow()
    {
        if (SelectedShow is not { } show) return;
        if (Save(() =>
            {
                if (IsFollowing)
                    _repository.UnfollowShow(show.CatalogId);
                else
                    _repository.FollowShow(new PodcastShow(show.CatalogId, show.Title, show.ArtworkUrl, DateTimeOffset.UtcNow));
            }))
            ReadLocalState();
    }

    [RelayCommand]
    private void TogglePlayed(PodcastEpisodeRow? episode)
    {
        if (episode is null) return;
        if (Save(() => _progress.SetPlayed(episode.VideoId, !episode.IsCompleted)))
            ReadLocalState();
    }

    [RelayCommand]
    private void PlayEpisode(PodcastEpisodeRow? episode)
    {
        if (episode is not { IsAvailable: true }) return;
        var context = SelectedShow is null ? new[] { episode } : Episodes.Where(row => row.IsAvailable).ToArray();
        var index = Array.FindIndex(context, row => row.VideoId == episode.VideoId);
        if (index >= 0) Play(context, index);
    }

    [RelayCommand]
    private void PlayUnplayed()
    {
        var rows = Episodes.Where(row => row.IsAvailable && !row.IsCompleted);
        if (UnplayedUsesPublicationOrder)
            rows = rows.OrderBy(row => row.State.Episode.PublishedAt);
        Play(rows.ToArray(), 0);
    }

    private void Play(PodcastEpisodeRow[] rows, int index)
    {
        if (rows.Length == 0) return;
        try
        {
            var tracks = rows.Select(row => _library.UpsertFromYouTube(row.VideoId, row.Title, row.ShowTitle,
                row.DurationSeconds, row.ArtworkUrl, mediaKind: TrackMediaKind.PodcastEpisode)).ToArray();
            _playback.PlayTrack(tracks[index], tracks, QueueSource.Podcast, selectedIndex: index);
            PlayFailed = false;
        }
        catch (Exception) { PlayFailed = true; }
    }

    private void ReadLocalState()
    {
        try
        {
            var followed = _repository.FollowedShows()
                .Select(show => new PodcastShowCard(show.CatalogId, show.Title, show.ArtworkUrl)).ToArray();
            if (!FollowedShows.SequenceEqual(followed)) FollowedShows = followed;
            var resume = _repository.ContinueEpisodes(12)
                .Select(entry => (entry.State, entry.Show.Title, entry.Show.ArtworkUrl));
            var continueRows = UpdateRows(ContinueEpisodes, resume);
            if (!ReferenceEquals(ContinueEpisodes, continueRows)) ContinueEpisodes = continueRows;
            if (SelectedShow is { } selected)
            {
                IsFollowing = FollowedShows.Any(show => show.CatalogId == selected.CatalogId);
                var states = _repository.Episodes(selected.CatalogId);
                if (_visibleEpisodeIds.Count > 0)
                {
                    var byId = states.ToDictionary(state => state.Episode.VideoId, StringComparer.Ordinal);
                    states = _visibleEpisodeIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
                }
                var rows = UpdateRows(Episodes, states.Select(state => (state, selected.Title, selected.ArtworkUrl)));
                if (!ReferenceEquals(Episodes, rows))
                    Episodes = rows;
                else
                {
                    OnPropertyChanged(nameof(CanPlayUnplayed));
                    OnPropertyChanged(nameof(UnplayedUsesPublicationOrder));
                    OnPropertyChanged(nameof(UnplayedOrderLabel));
                }
            }
        }
        catch (Exception) { _operationSaveFailed = true; }
        OnPropertyChanged(nameof(SaveFailed));
    }

    private static IReadOnlyList<PodcastEpisodeRow> UpdateRows(IReadOnlyList<PodcastEpisodeRow> previous,
        IEnumerable<(PodcastEpisodeState State, string Title, string? ArtworkUrl)> entries)
    {
        var byVideo = previous.ToDictionary(row => row.VideoId, StringComparer.Ordinal);
        var rows = new List<PodcastEpisodeRow>();
        foreach (var (state, title, artwork) in entries)
        {
            if (!byVideo.TryGetValue(state.Episode.VideoId, out var row) || row.ShowTitle != title || row.ShowArtwork != artwork)
                row = new PodcastEpisodeRow(state, title, artwork);
            else
                row.State = state;
            rows.Add(row);
        }
        return previous.SequenceEqual(rows) ? previous : rows;
    }

    private bool Save(Action action)
    {
        try
        {
            action();
            _operationSaveFailed = false;
        }
        catch (Exception) { _operationSaveFailed = true; }
        OnPropertyChanged(nameof(SaveFailed));
        return !_operationSaveFailed;
    }

    private static (string Language, string Region) Locale()
    {
        var language = L10n.Tr("en", "zh-CN", zhHant: "zh-TW", ja: "ja");
        var culture = CultureInfo.CurrentCulture;
        var region = culture.IsNeutralCulture || culture.Name.Length == 0 ? "US" : new RegionInfo(culture.Name).TwoLetterISORegionName;
        return (language, region);
    }
}
