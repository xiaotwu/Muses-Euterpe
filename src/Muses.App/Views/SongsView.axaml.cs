using Avalonia.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.L10n;

namespace Muses.App.Views;

public partial class SongsView : UserControl
{
    private ShellViewModel? _vm;

    public SongsView()
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
                RefreshSongs();
            }
        };

        Page.TrackActivated += row =>
        {
            if (_vm?.Playback is null || Page.Rows is null) return;
            var snapshots = Page.Rows.Select(r => r.Snapshot).ToList();
            _vm.Playback.PlayTrack(row.Snapshot, snapshots, QueueSource.Songs);
        };

        Page.PlayAllRequested += () =>
        {
            if (_vm?.Playback is null || Page.Rows is null || Page.Rows.Count == 0) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Songs);
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
            _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Songs);
        };

        PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                RefreshSongs();
            }
        };
    }

    public void RefreshSongs()
    {
        if (_vm?.Library is null) return;
        var tracks = _vm.Library.AllTracks();
        var rows = CollectionTrackRow.Songs(tracks);
        Page.TitleText = L10n.Tr("Songs", "歌曲");
        Page.SubtitleText = L10n.Tr($"{rows.Count} songs • Title A–Z", $"{rows.Count} 首歌曲 • 标题 A–Z");
        Page.EmptyTitle = L10n.Tr("No songs in library", "资料库中没有歌曲");
        Page.EmptySubtitle = L10n.Tr("Open Search (Ctrl+F) and paste a YouTube link", "打开搜索(Ctrl+F)并粘贴 YouTube 链接");
        Page.DefaultSort = CollectionTableDefaultSort.TitleAZ;
        Page.CurrentTrack = _vm.Playback?.State.Track;
        Page.Rows = rows;
    }
}
