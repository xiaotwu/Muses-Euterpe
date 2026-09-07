using Avalonia.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.L10n;

namespace Muses.App.Views;

public partial class RecentlyView : UserControl
{
    private ShellViewModel? _vm;

    public RecentlyView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel vm)
            {
                _vm = vm;
                Page.Playback = vm.Playback;
                Page.Library = vm.Library;
                Page.PlaylistService = vm.PlaylistService;
                RefreshRecently();
            }
        };

        Page.TrackActivated += row =>
        {
            if (_vm?.Playback is null || Page.Rows is null) return;
            var snapshots = Page.Rows.Select(r => r.Snapshot).ToList();
            _vm.Playback.PlayTrack(row.Snapshot, snapshots, QueueSource.Recently);
        };

        Page.PlayAllRequested += () =>
        {
            if (_vm?.Playback is null || Page.Rows is null || Page.Rows.Count == 0) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Recently);
        };

        Page.ShuffleRequested += () =>
        {
            if (_vm?.Playback is null || Page.Rows is null || Page.Rows.Count == 0) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            var rng = new Random();
            var n = snaps.Count;
            while (n > 1)
            {
                n--;
                var k = rng.Next(n + 1);
                (snaps[k], snaps[n]) = (snaps[n], snaps[k]);
            }
            _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Recently);
        };

        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                RefreshRecently();
            }
        };
    }

    public void RefreshRecently()
    {
        if (_vm?.Library is null) return;
        var tracks = _vm.Library.RecentlyPlayedTracks(100);
        // Preserve recency order from LibraryService (LastPlayedAt desc).
        var rows = tracks
            .Select((t, index) => new CollectionTrackRow(t, index))
            .ToList();
        Page.TitleText = L10n.Tr("Recently", "最近");
        Page.SubtitleText = L10n.Tr($"{rows.Count} recently played", $"{rows.Count} 首最近播放");
        Page.EmptyTitle = L10n.Tr("No recently played", "暂无最近播放");
        Page.EmptySubtitle = L10n.Tr("Play something to see it here", "播放歌曲后会出现在这里");
        Page.DefaultSort = CollectionTableDefaultSort.PlaylistOrder;
        Page.CurrentTrack = _vm.Playback?.State.Track;
        Page.Rows = rows;
    }
}
