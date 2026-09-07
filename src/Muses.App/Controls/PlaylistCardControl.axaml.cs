using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Muses.Core.L10n;
using Muses.App.Theme;

namespace Muses.App.Controls;

public partial class PlaylistCardControl : UserControl
{
    public event Action? Selected;
    public event Action? PlayClicked;
    public event Action? DeleteClicked;
    public event Action? TogglePinClicked;

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set => _isPinned = value;
    }

    public PlaylistCardControl()
    {
        InitializeComponent();

        PlayPillText.Text = L10n.Tr("Play", "播放");

        PointerEntered += (_, _) =>
        {
            CardBorder.RenderTransform = new TranslateTransform(0, -5);
            PlayPill.Background = ThemeBrushes.Accent;
        };

        PointerExited += (_, _) =>
        {
            CardBorder.RenderTransform = new TranslateTransform(0, 0);
            PlayPill.Background = ThemeBrushes.WhiteOverlay38;
        };

        PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                ShowContextMenu();
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                Selected?.Invoke();
            }
        };

        PlayPill.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(PlayPill).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                PlayClicked?.Invoke();
            }
        };
    }

    public void SetPlaylist(string name, int songCount, string? artworkUrl, bool isPinned)
    {
        _isPinned = isPinned;
        TitleBlock.Text = name;
        SubtitleBlock.Text = L10n.Tr($"Muses • {songCount} songs", $"Muses • {songCount} 首歌曲");
        TagBlock.Text = L10n.Tr("PLAYLIST", "歌单");
        YouTubeBadge.IsVisible = false;
        ArtControl.SourceUrl = artworkUrl;
    }

    public void SetYouTubeImport(string title, string channel, int songCount, string? artworkUrl)
    {
        TitleBlock.Text = title;
        var owner = string.IsNullOrEmpty(channel) ? L10n.Tr("Unknown owner", "未知所有者") : channel;
        SubtitleBlock.Text = L10n.Tr($"YouTube • {songCount} songs • {owner}", $"YouTube • {songCount} 首歌曲 • {owner}");
        TagBlock.Text = L10n.Tr("YOUTUBE", "YouTube");
        YouTubeBadge.IsVisible = true;
        ArtControl.SourceUrl = artworkUrl;
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem
        {
            Header = L10n.Tr("Open Playlist", "打开歌单"),
            Icon = new PathIcon { Data = Geometry.Parse("M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z"), Width = 13, Height = 13 }
        };
        openItem.Click += (_, _) => Selected?.Invoke();
        menu.Items.Add(openItem);

        var playItem = new MenuItem
        {
            Header = L10n.Tr("Play", "播放"),
            Icon = new PathIcon { Data = Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z"), Width = 13, Height = 13 }
        };
        playItem.Click += (_, _) => PlayClicked?.Invoke();
        menu.Items.Add(playItem);

        if (TogglePinClicked != null)
        {
            var pinItem = new MenuItem
            {
                Header = _isPinned ? L10n.Tr("Unpin", "取消钉选") : L10n.Tr("Pin", "钉选"),
                Icon = new PathIcon { Data = Geometry.Parse("M16,12V4H17V2H7V4H8V12L6,14V16H11V22H13V16H18V14L16,12Z"), Width = 13, Height = 13 }
            };
            pinItem.Click += (_, _) => TogglePinClicked?.Invoke();
            menu.Items.Add(pinItem);
        }

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = L10n.Tr("Delete", "删除"),
            Foreground = Brushes.Red,
            Icon = new PathIcon { Data = Geometry.Parse("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"), Width = 13, Height = 13 }
        };
        deleteItem.Click += (_, _) => DeleteClicked?.Invoke();
        menu.Items.Add(deleteItem);

        menu.Open(this);
    }
}
