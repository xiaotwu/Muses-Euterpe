using Avalonia.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Playback;

namespace Muses.App.Views;

public partial class PlaylistDetailView : UserControl
{
    private ShellViewModel? _vm;

    public PlaylistDetailView()
    {
        InitializeComponent();

        Page.BackRequested += () => _vm?.ClosePlaylistDetail();

        Page.TrackActivated += row =>
        {
            if (_vm?.Playback is null || Page.Rows is null) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            var source = _vm.SelectedYouTubeImport is not null ? QueueSource.Import : QueueSource.Playlist;
            _vm.Playback.PlayTrack(row.Snapshot, snaps, source);
        };

        Page.PlayAllRequested += () =>
        {
            if (_vm?.Playback is null || Page.Rows is null || Page.Rows.Count == 0) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            var source = _vm.SelectedYouTubeImport is not null ? QueueSource.Import : QueueSource.Playlist;
            _vm.Playback.PlayTrack(snaps[0], snaps, source);
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
            var source = _vm.SelectedYouTubeImport is not null ? QueueSource.Import : QueueSource.Playlist;
            _vm.Playback.PlayTrack(snaps[0], snaps, source);
        };

        Page.RemoveRequested += row =>
        {
            if (_vm is null) return;

            if (_vm.SelectedPlaylist is not null && _vm.PlaylistService is not null)
            {
                var full = _vm.PlaylistService.Get(_vm.SelectedPlaylist.Id);
                var item = full?.Items.FirstOrDefault(i => i.Id == row.Id || i.TrackId == row.Snapshot.Id);
                if (item is not null)
                {
                    _vm.PlaylistService.RemoveItem(item.Id);
                    Refresh();
                }
            }
            else if (_vm.SelectedYouTubeImport is not null && _vm.YouTubeImportService is not null)
            {
                var full = _vm.YouTubeImportService.Get(_vm.SelectedYouTubeImport.Id);
                var item = full?.Items.FirstOrDefault(i => i.Id == row.Id || i.TrackId == row.Snapshot.Id || i.YouTubeId == row.Snapshot.YouTubeId);
                if (item is not null)
                {
                    _vm.YouTubeImportService.RemoveItem(item.Id);
                    Refresh();
                }
            }
        };

        PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                Refresh();
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel vm)
            {
                _vm = vm;
                Page.Playback = vm.Playback;
                Page.Library = vm.Library;
                Page.PlaylistService = vm.PlaylistService;
                Refresh();
            }
        };
    }

    public void Refresh()
    {
        if (_vm is null) return;

        if (_vm.SelectedPlaylist is not null && _vm.PlaylistService is not null)
        {
            var pl = _vm.PlaylistService.Get(_vm.SelectedPlaylist.Id) ?? _vm.SelectedPlaylist;
            var rows = CollectionTrackRow.Playlist(pl.Items, id => _vm.Library?.Get(id));

            Page.TitleText = pl.Name;
            Page.SubtitleText = L10n.Tr($"{rows.Count} songs • Playlist Order", $"{rows.Count} 首歌曲 • 歌单顺序");
            Page.DefaultSort = CollectionTableDefaultSort.PlaylistOrder;
            Page.YouTubeUrl = null;
            Page.EmptyTitle = L10n.Tr("Playlist is empty", "歌单为空");
            Page.EmptySubtitle = L10n.Tr("Add YouTube songs from a song's context menu", "通过歌曲的右键菜单添加 YouTube 歌曲");
            Page.CurrentTrack = _vm.Playback?.State.Track;
            Page.Playlists = _vm.PlaylistService.FetchAll();
            Page.Rows = rows;
        }
        else if (_vm.SelectedYouTubeImport is not null && _vm.YouTubeImportService is not null)
        {
            var imp = _vm.YouTubeImportService.Get(_vm.SelectedYouTubeImport.Id) ?? _vm.SelectedYouTubeImport;
            var rows = CollectionTrackRow.YouTubeImport(imp.Items, id => _vm.Library?.Get(id));

            Page.TitleText = imp.Title;
            var channelInfo = string.IsNullOrEmpty(imp.Channel) ? "" : $"{imp.Channel} • ";
            Page.SubtitleText = $"{channelInfo}{rows.Count} {L10n.Tr("songs • Playlist Order", "首歌曲 • 歌单顺序")}";
            Page.DefaultSort = CollectionTableDefaultSort.PlaylistOrder;
            Page.YouTubeUrl = imp.Url;
            Page.EmptyTitle = L10n.Tr("Playlist is empty", "歌单为空");
            Page.EmptySubtitle = L10n.Tr("No songs in this YouTube playlist", "此 YouTube 歌单中没有歌曲");
            Page.CurrentTrack = _vm.Playback?.State.Track;
            Page.Playlists = _vm.PlaylistService?.FetchAll();
            Page.Rows = rows;
        }
    }
}
