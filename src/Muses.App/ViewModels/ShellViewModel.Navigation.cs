using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muses.Core.Catalog;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Preferences;
using Muses.Core.Search;

namespace Muses.App.ViewModels;

public enum SettingsCategory
{
    General,
    Playback,
    Quality,
    Appearance,
    YouTube,
    Lyrics,
    Desktop,
    About
}

public partial class ShellViewModel
{
    private sealed record BrowseRoute(
        SidebarSection Section,
        PlaylistEntity? Playlist = null,
        YouTubeImportEntity? Import = null,
        CatalogReleaseProjection? Release = null,
        CatalogArtistProjection? Artist = null,
        bool Settings = false);

    private readonly Stack<BrowseRoute> _browseBack = new();
    private readonly Stack<BrowseRoute> _browseForward = new();

    public string BackLabel => L10n.Tr("Back", "后退", zhHant: "返回");
    public string ForwardLabel => L10n.Tr("Forward", "前进", zhHant: "前進");
    public string NewNavigationLabel => L10n.Tr("New", "新内容", zhHant: "新內容");
    public string SearchPrompt => L10n.Tr("Artists, songs, albums, and videos", "艺术家、歌曲、专辑和视频", zhHant: "藝人、歌曲、專輯和影片");
    public string ClearSearchLabel => L10n.Tr("Clear Search", "清除搜索", zhHant: "清除搜尋");
    public string BrowseMusicLabel => L10n.Tr("Browse Your Music", "浏览你的音乐", zhHant: "瀏覽你的音樂");
    public string NoSearchResultsLabel => L10n.Tr("No results found", "未找到结果", zhHant: "未找到結果");
    public string SearchRetryLabel => L10n.Tr("Try another title, artist, album, or video.", "试试其他标题、艺术家、专辑或视频。", zhHant: "試試其他標題、藝人、專輯或影片。");
    public string SearchAllLabel => L10n.Tr("All", "全部");
    public bool CanNavigateBack => _browseBack.Count > 0;
    public bool CanNavigateForward => _browseForward.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsGeneral))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPlayback))]
    [NotifyPropertyChangedFor(nameof(IsSettingsQuality))]
    [NotifyPropertyChangedFor(nameof(IsSettingsAppearance))]
    [NotifyPropertyChangedFor(nameof(IsSettingsYouTube))]
    [NotifyPropertyChangedFor(nameof(IsSettingsLyrics))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDesktop))]
    [NotifyPropertyChangedFor(nameof(IsSettingsAbout))]
    private SettingsCategory _selectedSettingsCategory;

    public bool IsSettingsGeneral => SelectedSettingsCategory == SettingsCategory.General;
    public bool IsSettingsPlayback => SelectedSettingsCategory == SettingsCategory.Playback;
    public bool IsSettingsQuality => SelectedSettingsCategory == SettingsCategory.Quality;
    public bool IsSettingsAppearance => SelectedSettingsCategory == SettingsCategory.Appearance;
    public bool IsSettingsYouTube => SelectedSettingsCategory == SettingsCategory.YouTube;
    public bool IsSettingsLyrics => SelectedSettingsCategory == SettingsCategory.Lyrics;
    public bool IsSettingsDesktop => SelectedSettingsCategory == SettingsCategory.Desktop;
    public bool IsSettingsAbout => SelectedSettingsCategory == SettingsCategory.About;

    public event Action? SearchFocusRequested;

    private BrowseRoute CurrentBrowseRoute => new(
        SelectedSection, SelectedPlaylist, SelectedYouTubeImport, SelectedRelease, SelectedArtist, SettingsOpen);

    private void Navigate(BrowseRoute route)
    {
        var previous = CurrentBrowseRoute;
        if (route == previous) return;

        _browseBack.Push(previous);
        _browseForward.Clear();
        ApplyBrowseRoute(route);
    }

    private void ApplyBrowseRoute(BrowseRoute route)
    {
        SelectedPlaylist = route.Playlist;
        SelectedYouTubeImport = route.Import;
        SelectedRelease = route.Release;
        SelectedArtist = route.Artist;
        SelectedSection = route.Section;
        SettingsOpen = route.Settings;
        Podcasts?.SetActive(IsPodcasts && !SettingsOpen);
        OnPropertyChanged(nameof(PodcastsLabel));

        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        if (IsSearch && !SettingsOpen) SearchFocusRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (!_browseBack.TryPop(out var route)) return;

        _browseForward.Push(CurrentBrowseRoute);
        ApplyBrowseRoute(route);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private void NavigateForward()
    {
        if (!_browseForward.TryPop(out var route)) return;

        _browseBack.Push(CurrentBrowseRoute);
        ApplyBrowseRoute(route);
    }

    private void OpenSearch(string? query, GlobalSearchScope? scope)
    {
        if (GlobalSearch is not { } search) return;

        if (scope is { } requestedScope) search.Scope = requestedScope;
        if (query is not null) search.Query = query;
        if (IsSearch && !SettingsOpen)
            SearchFocusRequested?.Invoke();
        else
            Navigate(new BrowseRoute(SidebarSection.Search));
    }

    [RelayCommand]
    private void CloseSettings()
    {
        if (SettingsOpen) NavigateBack();
    }

    [RelayCommand]
    private void SelectSettingsCategory(SettingsCategory category) => SelectedSettingsCategory = category;

    public void ReturnFromSearch()
    {
        if (IsSearch) NavigateBack();
    }
}
