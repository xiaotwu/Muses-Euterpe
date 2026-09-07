using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Muses.Core.Chrome;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;
using Muses.Core.Playback;

namespace Muses.App.Controls.Collection;

public partial class CollectionPage : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<CollectionPage, string>(nameof(TitleText), "");

    public static readonly StyledProperty<string> SubtitleTextProperty =
        AvaloniaProperty.Register<CollectionPage, string>(nameof(SubtitleText), "");

    public static readonly StyledProperty<string?> YouTubeUrlProperty =
        AvaloniaProperty.Register<CollectionPage, string?>(nameof(YouTubeUrl));

    public static readonly StyledProperty<IReadOnlyList<CollectionTrackRow>?> RowsProperty =
        AvaloniaProperty.Register<CollectionPage, IReadOnlyList<CollectionTrackRow>?>(nameof(Rows));

    public static readonly StyledProperty<CollectionTableDefaultSort> DefaultSortProperty =
        AvaloniaProperty.Register<CollectionPage, CollectionTableDefaultSort>(nameof(DefaultSort), CollectionTableDefaultSort.TitleAZ);

    public static readonly StyledProperty<TrackSnapshot?> CurrentTrackProperty =
        AvaloniaProperty.Register<CollectionPage, TrackSnapshot?>(nameof(CurrentTrack));

    public static readonly StyledProperty<IReadOnlyList<PlaylistEntity>?> PlaylistsProperty =
        AvaloniaProperty.Register<CollectionPage, IReadOnlyList<PlaylistEntity>?>(nameof(Playlists));

    public static readonly StyledProperty<bool> ShowsBackButtonProperty =
        AvaloniaProperty.Register<CollectionPage, bool>(nameof(ShowsBackButton), false);

    public static readonly StyledProperty<string> EmptyTitleProperty =
        AvaloniaProperty.Register<CollectionPage, string>(nameof(EmptyTitle), "");

    public static readonly StyledProperty<string> EmptySubtitleProperty =
        AvaloniaProperty.Register<CollectionPage, string>(nameof(EmptySubtitle), "");

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string SubtitleText
    {
        get => GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public string? YouTubeUrl
    {
        get => GetValue(YouTubeUrlProperty);
        set => SetValue(YouTubeUrlProperty, value);
    }

    public IReadOnlyList<CollectionTrackRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public CollectionTableDefaultSort DefaultSort
    {
        get => GetValue(DefaultSortProperty);
        set => SetValue(DefaultSortProperty, value);
    }

    public TrackSnapshot? CurrentTrack
    {
        get => GetValue(CurrentTrackProperty);
        set => SetValue(CurrentTrackProperty, value);
    }

    public IReadOnlyList<PlaylistEntity>? Playlists
    {
        get => GetValue(PlaylistsProperty);
        set => SetValue(PlaylistsProperty, value);
    }

    public bool ShowsBackButton
    {
        get => GetValue(ShowsBackButtonProperty);
        set => SetValue(ShowsBackButtonProperty, value);
    }

    public string EmptyTitle
    {
        get => GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public string EmptySubtitle
    {
        get => GetValue(EmptySubtitleProperty);
        set => SetValue(EmptySubtitleProperty, value);
    }

    public LibraryService? Library
    {
        get => TrackTable.Library;
        set => TrackTable.Library = value;
    }

    public PlaybackService? Playback
    {
        get => TrackTable.Playback;
        set => TrackTable.Playback = value;
    }

    public PlaylistService? PlaylistService
    {
        get => TrackTable.PlaylistService;
        set => TrackTable.PlaylistService = value;
    }

    public event Action<CollectionTrackRow>? TrackActivated;
    public event Action? BackRequested;
    public event Action<CollectionTrackRow>? RemoveRequested;
    public event Action? PlayAllRequested;
    public event Action? ShuffleRequested;

    private CollectionPageMode _mode = CollectionPageMode.Stage;

    public CollectionPage()
    {
        InitializeComponent();

        BackButton.Click += (_, _) => BackRequested?.Invoke();
        PlayAllButton.Click += (_, _) => PlayAll();
        ShuffleButton.Click += (_, _) => ShuffleAll();

        YouTubeLinkButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(YouTubeUrl))
            {
                try { Process.Start(new ProcessStartInfo(YouTubeUrl) { UseShellExecute = true }); }
                catch { }
            }
        };

        SongDeck.TrackActivated += row => TrackActivated?.Invoke(row);
        SongDeck.ExpandRequested += () => SetMode(CollectionPageMode.List);
        SongDeck.RemoveRequested += row => RemoveRequested?.Invoke(row);

        TrackTable.TrackActivated += row => TrackActivated?.Invoke(row);
        TrackTable.CollapseRequested += () => SetMode(CollectionPageMode.Stage);
        TrackTable.RemoveRequested += row => RemoveRequested?.Invoke(row);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleTextProperty)
        {
            PageTitle.Text = change.GetNewValue<string>();
            EmptyPageTitle.Text = change.GetNewValue<string>();
        }
        else if (change.Property == SubtitleTextProperty)
        {
            PageSubtitle.Text = change.GetNewValue<string>();
            EmptyPageSubtitle.Text = change.GetNewValue<string>();
        }
        else if (change.Property == YouTubeUrlProperty)
        {
            var url = change.GetNewValue<string?>();
            YouTubeLinkButton.IsVisible = !string.IsNullOrEmpty(url);
        }
        else if (change.Property == RowsProperty)
        {
            var rows = change.GetNewValue<IReadOnlyList<CollectionTrackRow>?>();
            SongDeck.Rows = rows;
            TrackTable.Rows = rows;
            UpdateEmptyState(rows);
        }
        else if (change.Property == DefaultSortProperty)
        {
            TrackTable.DefaultSort = change.GetNewValue<CollectionTableDefaultSort>();
        }
        else if (change.Property == CurrentTrackProperty)
        {
            var track = change.GetNewValue<TrackSnapshot?>();
            SongDeck.CurrentTrack = track;
            TrackTable.CurrentTrack = track;
        }
        else if (change.Property == PlaylistsProperty)
        {
            var playlists = change.GetNewValue<IReadOnlyList<PlaylistEntity>?>();
            SongDeck.Playlists = playlists;
            TrackTable.Playlists = playlists;
        }
        else if (change.Property == ShowsBackButtonProperty)
        {
            BackButton.IsVisible = change.GetNewValue<bool>();
        }
        else if (change.Property == EmptyTitleProperty)
        {
            EmptyTitleText.Text = change.GetNewValue<string>();
        }
        else if (change.Property == EmptySubtitleProperty)
        {
            EmptySubtitleText.Text = change.GetNewValue<string>();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && _mode == CollectionPageMode.List)
        {
            SetMode(CollectionPageMode.Stage);
            e.Handled = true;
        }
    }

    private void UpdateEmptyState(IReadOnlyList<CollectionTrackRow>? rows)
    {
        var isEmpty = rows is null || rows.Count == 0;
        EmptyPanel.IsVisible = isEmpty;
        ContentPanel.IsVisible = !isEmpty;
    }

    public void SetMode(CollectionPageMode mode)
    {
        _mode = mode;
        if (mode == CollectionPageMode.Stage)
        {
            ListViewPanel.Opacity = 0;
            ListViewPanel.IsVisible = false;
            StageViewPanel.Opacity = 1;
            StageViewPanel.IsVisible = true;
            SongDeck.Focus();
        }
        else
        {
            StageViewPanel.Opacity = 0;
            StageViewPanel.IsVisible = false;
            ListViewPanel.Opacity = 1;
            ListViewPanel.IsVisible = true;
            TrackTable.Focus();
        }
    }

    private void PlayAll()
    {
        if (PlayAllRequested is not null)
        {
            PlayAllRequested.Invoke();
            return;
        }
        var rows = Rows;
        if (rows is null || rows.Count == 0) return;
        TrackActivated?.Invoke(rows[0]);
    }

    private void ShuffleAll()
    {
        if (ShuffleRequested is not null)
        {
            ShuffleRequested.Invoke();
            return;
        }
        var rows = Rows;
        if (rows is null || rows.Count == 0) return;
        var list = rows.ToList();
        var rng = new Random();
        var n = list.Count;
        while (n > 1)
        {
            n--;
            var k = rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
        TrackActivated?.Invoke(list[0]);
    }
}
