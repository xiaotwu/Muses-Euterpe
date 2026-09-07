using System.Diagnostics;
using Avalonia.Controls;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Library;
using Muses.Core.Playback;

namespace Muses.App.Controls;

public static class TrackContextMenu
{
    public static ContextMenu Create(
        TrackSnapshot snapshot,
        PlaybackService? playback,
        LibraryService? library,
        PlaylistService? playlistService,
        TrackEntity? track = null,
        Action? onPlay = null,
        Action? onRemoveFromContainer = null,
        Action? onNewPlaylist = null)
    {
        var menu = new ContextMenu();

        // Play
        var playItem = new MenuItem
        {
            Header = L10n.Tr("Play", "播放"),
            Icon = CreatePathIcon("M8,5.14V19.14L19,12.14L8,5.14Z")
        };
        playItem.Click += (_, _) =>
        {
            if (onPlay != null) onPlay();
            else playback?.PlayTrack(snapshot, [snapshot], QueueSource.Search);
        };
        menu.Items.Add(playItem);

        // Play Next
        var playNextItem = new MenuItem
        {
            Header = L10n.Tr("Play Next", "下一首播放"),
            Icon = CreatePathIcon("M14,10H2V12H14V10M14,6H2V8H14V6M2,16H10V14H2V16M21.5,11.5L23,13L16,20L11.5,15.5L13,14L16,17L21.5,11.5Z")
        };
        playNextItem.Click += (_, _) => playback?.Queue.PlayNext(snapshot);
        menu.Items.Add(playNextItem);

        // Add to Queue
        var addToQueueItem = new MenuItem
        {
            Header = L10n.Tr("Add to Queue", "加入队列"),
            Icon = CreatePathIcon("M3,13H15V11H3M3,6V8H21V6M3,18H9V16H3V18Z")
        };
        addToQueueItem.Click += (_, _) => playback?.Queue.AddToQueue(snapshot);
        menu.Items.Add(addToQueueItem);

        // YouTube Link options
        if (!string.IsNullOrEmpty(snapshot.YouTubeId))
        {
            var copyLinkItem = new MenuItem
            {
                Header = L10n.Tr("Copy Link", "复制链接"),
                Icon = CreatePathIcon("M3.9,12C3.9,10.29 5.29,8.9 7,8.9H11V7H7A5,5 0 0,0 2,12A5,5 0 0,0 7,17H11V15.1H7C5.29,15.1 3.9,13.71 3.9,12M8,13H16V11H8V13M17,7H13V8.9H17C18.71,8.9 20.1,10.29 20.1,12C20.1,13.71 18.71,15.1 17,15.1H13V17H17A5,5 0 0,0 22,12A5,5 0 0,0 17,7Z")
            };
            copyLinkItem.Click += async (_, _) =>
            {
                var top = TopLevel.GetTopLevel(menu);
                if (top?.Clipboard is not null)
                {
                    await top.Clipboard.SetTextAsync($"https://youtu.be/{snapshot.YouTubeId}");
                }
            };
            menu.Items.Add(copyLinkItem);

            var openYtItem = new MenuItem
            {
                Header = L10n.Tr("Open on YouTube", "在 YouTube 打开"),
                Icon = new YouTubeMark { MarkSize = 12 }
            };
            openYtItem.Click += (_, _) =>
            {
                try
                {
                    var url = $"https://youtu.be/{snapshot.YouTubeId}";
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            };
            menu.Items.Add(openYtItem);
        }

        // Persistent track items
        var resolvedTrack = track;
        if (resolvedTrack is not null || library is not null)
        {
            menu.Items.Add(new Separator());

            // Like / Unlike
            var isLiked = snapshot.Liked;
            var likeItem = new MenuItem
            {
                Header = isLiked ? L10n.Tr("Unlike", "取消收藏") : L10n.Tr("Like", "收藏"),
                Icon = CreatePathIcon(isLiked
                    ? "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z"
                    : "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z")
            };
            likeItem.Click += (_, _) => library?.ToggleLike(snapshot.Id);
            menu.Items.Add(likeItem);

            // Add to playlist
            if (playlistService is not null)
            {
                var addToPlaylistItem = new MenuItem
                {
                    Header = L10n.Tr("Add to Playlist", "添加到歌单"),
                    Icon = CreatePathIcon("M14,10H2V12H14V10M14,6H2V8H14V6M14,14H2V16H14V14M18,14V10H16V14H12V16H16V20H18V16H22V14H18Z")
                };

                if (onNewPlaylist != null)
                {
                    var newPlaylistItem = new MenuItem
                    {
                        Header = L10n.Tr("New Playlist…", "新建歌单…"),
                        Icon = CreatePathIcon("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z")
                    };
                    newPlaylistItem.Click += (_, _) => onNewPlaylist();
                    addToPlaylistItem.Items.Add(newPlaylistItem);
                }

                var allPlaylists = playlistService.FetchAll();
                if (allPlaylists.Count > 0 && onNewPlaylist != null)
                {
                    addToPlaylistItem.Items.Add(new Separator());
                }

                foreach (var pl in allPlaylists)
                {
                    var plItem = new MenuItem { Header = pl.Name };
                    var targetPl = pl;
                    plItem.Click += (_, _) =>
                    {
                        var t = resolvedTrack ?? new TrackEntity
                        {
                            Id = snapshot.Id,
                            Title = snapshot.Title,
                            Artist = snapshot.Artist,
                            AlbumTitle = snapshot.AlbumTitle,
                            DurationMs = (int)Math.Round(snapshot.DurationSeconds * 1000),
                            YouTubeId = snapshot.YouTubeId,
                            ArtworkUrl = snapshot.ArtworkUrl
                        };
                        playlistService.AddTrack(targetPl, t);
                    };
                    addToPlaylistItem.Items.Add(plItem);
                }

                menu.Items.Add(addToPlaylistItem);
            }
        }

        // Remove from container
        if (onRemoveFromContainer != null)
        {
            menu.Items.Add(new Separator());
            var removeItem = new MenuItem
            {
                Header = L10n.Tr("Remove from Playlist", "从歌单移除"),
                Foreground = Avalonia.Media.Brushes.Red,
                Icon = CreatePathIcon("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z")
            };
            removeItem.Click += (_, _) => onRemoveFromContainer();
            menu.Items.Add(removeItem);
        }

        return menu;
    }

    private static PathIcon CreatePathIcon(string data) => new()
    {
        Data = Avalonia.Media.Geometry.Parse(data),
        Width = 14,
        Height = 14
    };
}
