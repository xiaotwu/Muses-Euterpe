using Avalonia.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Playback;

namespace Muses.App.Views;

public partial class CatalogReleaseDetailView : UserControl
{
    private ShellViewModel? _vm;

    public CatalogReleaseDetailView()
    {
        InitializeComponent();

        Page.BackRequested += () => _vm?.CloseReleaseDetail();

        Page.TrackActivated += row =>
        {
            if (_vm?.Playback is null || Page.Rows is null) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            _vm.Playback.PlayTrack(row.Snapshot, snaps, QueueSource.Album);
        };

        Page.PlayAllRequested += () =>
        {
            if (_vm?.Playback is null || Page.Rows is null || Page.Rows.Count == 0) return;
            var snaps = Page.Rows.Select(r => r.Snapshot).ToList();
            _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Album);
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
            _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Album);
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
        if (_vm?.SelectedRelease is null) return;
        var rel = _vm.SelectedRelease;

        var rows = rel.Tracks
            .Select((t, index) => new CollectionTrackRow(t, index, trackNumber: index + 1))
            .ToList();

        Page.TitleText = rel.Title;
        var yearText = rel.Year is not null ? $" • {rel.Year}" : "";
        Page.SubtitleText = $"{rel.ArtistName}{yearText} • {rows.Count} {L10n.Tr("songs", "首歌曲")}";
        Page.DefaultSort = CollectionTableDefaultSort.PlaylistOrder;
        Page.CurrentTrack = _vm.Playback?.State.Track;
        Page.Playlists = _vm.PlaylistService?.FetchAll();
        Page.Rows = rows;
    }
}
