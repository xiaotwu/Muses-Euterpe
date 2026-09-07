using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Domain;
using Muses.Core.L10n;
using Muses.Core.Playback;
using Muses.Core.YouTube;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class PlaylistsOverviewView : UserControl
{
    private ShellViewModel? _vm;

    public PlaylistsOverviewView()
    {
        InitializeComponent();

        PageTitleText.Text = L10n.Tr("All Playlists", "全部歌单");
        EmptyTitleText.Text = L10n.Tr("No playlists", "没有歌单");
        EmptySubtitleText.Text = L10n.Tr("Create a Muses playlist or import one from YouTube.", "创建 Muses 歌单或从 YouTube 导入。");
        EmptyCreateButton.Content = L10n.Tr("New Playlist", "新建歌单");
        EmptyImportButton.Content = L10n.Tr("Import YouTube Playlist", "导入 YouTube 歌单");
        RecentDeletedTitle.Text = L10n.Tr("Recently Deleted", "最近删除");
        RecentDeletedSubtitle.Text = L10n.Tr(
            "Local recovery is available for 30 days. Restoring never pushes to YouTube.",
            "本地恢复保留 30 天。恢复不会推送至 YouTube。");
        UndoButton.Content = L10n.Tr("Undo", "撤销");

        AddButton.Click += (_, _) => _vm?.OpenAddChoice();
        EmptyCreateButton.Click += (_, _) => _vm?.OpenNewPlaylistDialog();
        EmptyImportButton.Click += (_, _) => _vm?.OpenImportDialog();
        UndoButton.Click += (_, _) =>
        {
            _vm?.UndoPlaylistDeletion();
            Refresh();
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
                _vm.PlaylistsChanged -= Refresh;
                _vm.PlaylistsChanged += Refresh;
                Refresh();
            }
        };
    }

    public void Refresh()
    {
        if (_vm?.PlaylistService is null || _vm.YouTubeImportService is null) return;

        UpdateUndoBanner();

        var playlists = _vm.PlaylistService.FetchAll();
        var activeImports = _vm.YouTubeImportService.FetchActive();
        var deletedImports = _vm.YouTubeImportService.FetchDeleted();

        var isEmpty = playlists.Count == 0 && activeImports.Count == 0 && deletedImports.Count == 0;
        EmptyPanel.IsVisible = isEmpty;
        CardsWrapPanel.IsVisible = !isEmpty;

        CardsWrapPanel.Children.Clear();

        // 1. User playlists
        foreach (var playlist in playlists)
        {
            var card = new PlaylistCardControl();
            string? artUrl = null;
            if (playlist.Items.Count > 0)
            {
                var itemWithArt = playlist.Items.FirstOrDefault(i => !string.IsNullOrEmpty(i.Track?.ArtworkUrl));
                artUrl = itemWithArt?.Track?.ArtworkUrl;
            }

            card.SetPlaylist(playlist.Name, playlist.Items.Count, artUrl, playlist.Pinned);

            var pl = playlist;
            card.Selected += () => _vm.SelectPlaylist(pl);
            card.PlayClicked += () =>
            {
                var full = _vm.PlaylistService.Get(pl.Id) ?? pl;
                var snaps = full.Items
                    .Where(i => i.Track is not null && !string.IsNullOrEmpty(i.Track.YouTubeId))
                    .OrderBy(i => i.ItemOrder)
                    .Select(i => i.Track!.ToSnapshot())
                    .ToList();
                if (snaps.Count > 0 && _vm.Playback is not null)
                {
                    _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Playlist);
                }
            };
            card.TogglePinClicked += () =>
            {
                _vm.PlaylistService.TogglePin(pl.Id);
                Refresh();
                _vm.RefreshSidebar();
            };
            card.DeleteClicked += () =>
            {
                var snapshot = _vm.PlaylistService.DeleteWithUndoSnapshot(pl.Id);
                if (snapshot is not null)
                {
                    _vm.SetUndoableDeletion(snapshot);
                }
                Refresh();
                _vm.RefreshSidebar();
            };

            card.Margin = new Thickness(0, 0, 24, 30);
            CardsWrapPanel.Children.Add(card);
        }

        // 2. Active YouTube imports
        foreach (var imp in activeImports)
        {
            var card = new PlaylistCardControl();
            card.Margin = new Thickness(0, 0, 24, 30);
            card.SetYouTubeImport(imp.Title, imp.Channel, imp.Items.Count, imp.ArtworkUrl);

            var ym = imp;
            card.Selected += () => _vm.SelectYouTubeImport(ym);
            card.PlayClicked += () =>
            {
                var full = _vm.YouTubeImportService.Get(ym.Id) ?? ym;
                var snaps = full.Items
                    .OrderBy(i => i.ItemOrder)
                    .Select(i =>
                    {
                        if (i.Track is not null && !string.IsNullOrEmpty(i.Track.YouTubeId))
                            return i.Track.ToSnapshot();
                        return new TrackSnapshot(
                            i.TrackId ?? i.Id,
                            string.IsNullOrEmpty(i.Title) ? i.YouTubeId : i.Title,
                            "",
                            null,
                            0,
                            i.YouTubeId,
                            YouTubeLinks.ThumbnailUrl(i.YouTubeId));
                    })
                    .ToList();
                if (snaps.Count > 0 && _vm.Playback is not null)
                {
                    _vm.Playback.PlayTrack(snaps[0], snaps, QueueSource.Import);
                }
            };
            card.DeleteClicked += () =>
            {
                _vm.YouTubeImportService.MoveToRecentlyDeleted(ym.Id);
                Refresh();
                _vm.RefreshSidebar();
            };

            CardsWrapPanel.Children.Add(card);
        }

        // 3. Recently Deleted section
        if (deletedImports.Count > 0)
        {
            RecentlyDeletedSection.IsVisible = true;
            RecentDeletedItemsPanel.Children.Clear();

            foreach (var deleted in deletedImports)
            {
                var del = deleted;
                var row = CreateRecentlyDeletedRow(del);
                RecentDeletedItemsPanel.Children.Add(row);
            }
        }
        else
        {
            RecentlyDeletedSection.IsVisible = false;
        }
    }

    private void UpdateUndoBanner()
    {
        if (_vm?.UndoablePlaylistDeletion is not null)
        {
            UndoBanner.IsVisible = true;
            UndoMessageText.Text = L10n.Tr(
                $"{_vm.UndoablePlaylistDeletion.Name} was deleted",
                $"已删除 {_vm.UndoablePlaylistDeletion.Name}");
        }
        else
        {
            UndoBanner.IsVisible = false;
        }
    }

    private Control CreateRecentlyDeletedRow(YouTubeImportEntity item)
    {
        var border = new Border
        {
            Background = ThemeBrushes.SurfaceBrush,
            BorderBrush = ThemeBrushes.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var infoPanel = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleText = new TextBlock
        {
            Text = item.Title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.TextPrimaryBrush
        };

        var daysRemaining = 30;
        if (item.DeletedAt.HasValue)
        {
            var elapsedDays = (DateTimeOffset.UtcNow - item.DeletedAt.Value).TotalDays;
            daysRemaining = Math.Max(0, 30 - (int)elapsedDays);
        }

        var subtitleText = new TextBlock
        {
            Text = L10n.Tr(
                $"{daysRemaining} days remaining in local retention",
                $"本地保留剩余 {daysRemaining} 天"),
            FontSize = 12,
            Foreground = ThemeBrushes.TextSecondaryBrush
        };

        infoPanel.Children.Add(titleText);
        infoPanel.Children.Add(subtitleText);
        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var restoreBtn = new Button
        {
            Content = L10n.Tr("Restore", "恢复"),
            Background = ThemeBrushes.WhiteOverlay38,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 5),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        restoreBtn.Click += (_, _) =>
        {
            _vm?.YouTubeImportService?.RestoreFromRecentlyDeleted(item.Id);
            Refresh();
            _vm?.RefreshSidebar();
        };

        var purgeBtn = new Button
        {
            Content = L10n.Tr("Delete Permanently", "彻底删除"),
            Background = Brushes.Transparent,
            Foreground = ThemeBrushes.Accent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        purgeBtn.Click += (_, _) =>
        {
            _vm?.YouTubeImportService?.PermanentlyDelete(item.Id);
            Refresh();
            _vm?.RefreshSidebar();
        };

        buttonPanel.Children.Add(restoreBtn);
        buttonPanel.Children.Add(purgeBtn);
        Grid.SetColumn(buttonPanel, 1);
        grid.Children.Add(buttonPanel);

        border.Child = grid;
        return border;
    }
}
