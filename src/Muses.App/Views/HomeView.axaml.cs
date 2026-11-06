using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Controls;
using Muses.App.ViewModels;
using Muses.Core.Discovery;
using Muses.Core.Domain;
using Muses.App.Theme;
using Muses.Core.L10n;

namespace Muses.App.Views;

public partial class HomeView : UserControl
{
    private ShellViewModel? _vm;

    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => OnDataContextChanged(null, EventArgs.Empty);
        DetachedFromVisualTree += (_, _) => Disconnect();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && IsVisible)
                UpdateDiscoveryView();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Disconnect();
        _vm = DataContext as ShellViewModel;
        if (_vm is not { } vm) return;
        if (vm.Library is { } library)
            library.Changed += OnLocalDiscoveryChanged;
        if (vm.HomeDiscovery is { } discovery)
            discovery.Changed += OnDiscoveryChanged;
        vm.PropertyChanged += OnHomePropertyChanged;
        UpdateDiscoveryView();
    }

    private void Disconnect()
    {
        if (_vm?.Library is { } library)
            library.Changed -= OnLocalDiscoveryChanged;
        if (_vm?.HomeDiscovery is { } discovery)
            discovery.Changed -= OnDiscoveryChanged;
        if (_vm is not null)
            _vm.PropertyChanged -= OnHomePropertyChanged;
        _vm = null;
        TopPicksContainer.Children.Clear();
        ShelvesContainer.Children.Clear();
    }

    private void OnHomePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsAccountHomeSelected) or nameof(ShellViewModel.WebHomeEnabled)
            or nameof(ShellViewModel.WebHomeStatusText) or nameof(ShellViewModel.AccountDisplayName)
            or nameof(ShellViewModel.IsAccountSignedIn) or nameof(ShellViewModel.HomeLabel))
            UpdateDiscoveryView();
    }

    private void OnDiscoveryChanged()
    {
        Dispatcher.UIThread.Post(UpdateDiscoveryView);
    }

    private void OnLocalDiscoveryChanged()
    {
        if (_vm?.IsAccountHomeSelected != true)
            OnDiscoveryChanged();
    }

    private void UpdateDiscoveryView()
    {
        if (!IsVisible || _vm is not { } vm) return;

        var accountSelected = vm.IsAccountHomeSelected;
        var hasAccountConsent = vm.IsAccountSignedIn && vm.WebHome is { IsEnabled: true, HasConsent: true };
        var sections = accountSelected
            ? (hasAccountConsent ? vm.HomeDiscovery?.Sections : null) ?? []
            : vm.Situational?.Compute().Select(section => new HomeSection(
                section.Id, section.Title, section.Subtitle,
                section.Id == "time-band" ? HomeSectionKind.QuickPicks : HomeSectionKind.AlbumCarousel,
                section.Items.Select(track => (DiscoveryItem)new DiscoveryItem.Track(track)).ToArray(),
                source: HomeSource.LocalLibrary)).ToArray() ?? [];
        var hasSections = sections.Any(section => section.Items.Count > 0);
        var loading = accountSelected && hasAccountConsent && !hasSections && vm.HomeDiscovery?.IsRefreshing == true;
        HomeSourceLabel.Text = accountSelected ? vm.HomeYouTubeSourceLabel : vm.HomeMusesSourceLabel;
        AutomationProperties.SetName(HomeSourceButton, string.Format(
            L10n.Tr("Home source: {0}", "首页来源：{0}", "首頁來源：{0}"), HomeSourceLabel.Text));
        HomeSourceStatus.Text = accountSelected
            ? hasSections
                ? string.Join(" · ", new[] { HomeSource.SignedInWeb.Label(), vm.AccountDisplayName }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                : vm.WebHomeStatusText
            : L10n.Tr("Recommended privately on this device", "由此设备私密推荐", "由此裝置私密推薦");
        HomeSourceStatus.IsVisible = !accountSelected || hasSections;
        LoadingAccountHome.IsVisible = loading;
        EmptyAccountHome.IsVisible = accountSelected && !loading && !hasSections;
        RetryAccountHome.IsVisible = hasAccountConsent;
        if (EmptyAccountHome.IsVisible)
            UpdateAccountEmptyState(vm, hasAccountConsent);

        EmptyHomeTitle.Text = L10n.Tr("Make Home yours", "打造你的首页");
        EmptyHomeBody.Text = L10n.Tr("Save songs to your library to get recommendations from your own collection.",
            "将歌曲保存到资料库，即可从自己的收藏中获得推荐。");
        EmptyHome.IsVisible = !accountSelected && !hasSections;
        var firstSection = sections.FirstOrDefault(s => s.Items.Count > 0);
        var hero = firstSection?.Items.FirstOrDefault();
        var mixed = sections.Skip(1).Take(2).SelectMany(s => s.Items);
        var recent = sections.TakeLast(2).SelectMany(s => s.Items);

        var topPicks = accountSelected
            ? TopPicksResolver.Picks(null, sections.SelectMany(section => section.Items), [], max: 6)
            : TopPicksResolver.Picks(hero, mixed, recent, max: 6);
        TopPicksContainer.Children.Clear();
        foreach (var pick in topPicks)
        {
            TopPicksContainer.Children.Add(BuildPortraitCard(pick, topPicks));
        }
        TopPicksSection.IsVisible = topPicks.Count > 0;

        ShelvesContainer.Children.Clear();
        foreach (var section in sections)
        {
            if (section.Items.Count == 0) continue;

            var shelf = BuildShelf(section);
            ShelvesContainer.Children.Add(shelf);
        }
    }

    private void UpdateAccountEmptyState(ShellViewModel vm, bool hasAccountConsent)
    {
        (EmptyAccountHomeTitle.Text, EmptyAccountHomeBody.Text) = !vm.IsAccountSignedIn
            ? (L10n.Tr("Connect YouTube", "连接 YouTube", "連接 YouTube"),
                L10n.Tr("Connect your YouTube account, then review browser access for Personalized Home.",
                    "连接你的 YouTube 账号，然后查看个性化首页的浏览器访问说明。",
                    "連接你的 YouTube 帳號，然後查看個人化首頁的瀏覽器存取說明。"))
            : !hasAccountConsent
                ? (L10n.Tr("Set up Personalized Home", "设置个性化首页", "設定個人化首頁"),
                    L10n.Tr("Allow browser access in YouTube Account settings to see your recommendations.",
                        "在 YouTube 账号设置中允许浏览器访问，即可查看你的推荐。",
                        "在 YouTube 帳號設定中允許瀏覽器存取，即可查看你的推薦。"))
                : (L10n.Tr("Account recommendations unavailable", "账号推荐暂不可用", "帳號推薦暫不可用"),
                    vm.WebHomeStatusText);
    }

    private void OnSelectMusesHome(object? sender, RoutedEventArgs e) => _vm?.SetHomeSource(false);

    private void OnSelectAccountHome(object? sender, RoutedEventArgs e) => _vm?.SetHomeSource(true);

    private void OnRetryAccountHome(object? sender, RoutedEventArgs e) => _vm?.RefreshAccountHome();

    private void OnOpenHomeAccountSettings(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { } vm) return;
        vm.SelectedSettingsCategory = SettingsCategory.YouTube;
        vm.OpenSettingsCommand.Execute(null);
    }

    private Control BuildShelf(HomeSection section)
    {
        var panel = new StackPanel { Spacing = 13 };

        // Header
        var header = new StackPanel { Spacing = 3 };
        header.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeBrushes.TextPrimaryBrush
        });
        var subtitle = section.Source == HomeSource.SignedInWeb
            ? string.IsNullOrWhiteSpace(section.Subtitle) ? section.Source.Label() : $"{section.Subtitle} · {section.Source.Label()}"
            : section.Subtitle;
        if (!string.IsNullOrEmpty(subtitle))
        {
            header.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 13,
                Foreground = ThemeBrushes.TextSecondaryBrush
            });
        }
        panel.Children.Add(header);

        if (section.Kind is HomeSectionKind.QuickPicks or HomeSectionKind.SongGrid && section.Items.All(CanPlay))
        {
            // 2-column grid of quick picks
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
            };

            var items = section.Items.Take(8).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var row = i % 4;
                var col = i / 4;

                var btn = BuildQuickRow(item, section.Items);
                btn.Margin = new Avalonia.Thickness(col == 0 ? 0 : 8, 4, col == 0 ? 8 : 0, 4);
                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                grid.Children.Add(btn);
            }
            panel.Children.Add(grid);
        }
        else
        {
            // Horizontal carousel
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 0, 56, 0) };
            foreach (var item in section.Items)
            {
                row.Children.Add(BuildSquareCard(item, section.Items));
            }
            scroll.Content = row;
            panel.Children.Add(WrapHorizontalFade(scroll));
        }

        return panel;
    }

    private static Control WrapHorizontalFade(Control inner)
    {
        var host = new Panel();
        host.Children.Add(inner);
        var page = ThemeBrushes.Page as ISolidColorBrush;
        var c = page?.Color ?? Color.FromRgb(0x1F, 0x1F, 0x1F);
        host.Children.Add(new Border
        {
            Width = 56,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(230, c.R, c.G, c.B), 1)
                }
            }
        });
        return host;
    }

    private Control BuildSquareCard(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings) =>
        BuildArtworkCard(item, siblings, AlbumCardPresentationStyle.Standard, 184);

    private Control BuildQuickRow(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings)
    {
        var (title, uploader, artworkUrl) = ExtractItemDisplay(item);
        var row = new CompactTrackRowControl
        {
            TrackTitle = title,
            TrackArtist = uploader,
            ArtworkUrl = artworkUrl,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.PlayRequested += (_, _) => PlayDiscoveryItem(item, siblings);
        EnableKeyboardAction(row, title, () =>
        {
            PlayDiscoveryItem(item, siblings);
            return Task.CompletedTask;
        });
        return row;
    }

    private static (string Title, string Uploader, string? ArtworkUrl) ExtractItemDisplay(DiscoveryItem item)
    {
        return item switch
        {
            DiscoveryItem.YouTube yt => (yt.Card.Title, GetYouTubeCardSubtitle(yt.Card), yt.Card.ThumbnailUrl),
            DiscoveryItem.Track tr => (tr.Snapshot.Title, tr.Snapshot.Artist, tr.Snapshot.ArtworkUrl),
            _ => ("", "", null)
        };
    }

    private static string GetYouTubeCardSubtitle(YouTubeDiscoveryCard card)
    {
        var uploader = card.Uploader ?? "YouTube Music";
        var availability = card.Availability switch
        {
            HomeCardAvailability.Available => null,
            HomeCardAvailability.RegionBlocked => L10n.Tr("Unavailable in your region", "在你的地区不可用", "在你的地區無法使用"),
            HomeCardAvailability.PrivateItem => L10n.Tr("Private", "私密", "私人"),
            HomeCardAvailability.Deleted => L10n.Tr("Deleted", "已删除", "已刪除"),
            _ => L10n.Tr("Unavailable", "不可用", "無法使用")
        };
        return availability is null ? uploader : $"{uploader} · {availability}";
    }

    private static bool CanPlay(DiscoveryItem item) => item is DiscoveryItem.Track or
        DiscoveryItem.YouTube { Card.PlayableVideoId: { Length: > 0 } };

    private bool IsCurrentAccountItem(DiscoveryItem item) => _vm is
        { IsAccountHomeSelected: true, IsAccountSignedIn: true, WebHome: { IsEnabled: true, HasConsent: true } } vm &&
        vm.HomeDiscovery?.Sections.Any(section => section.Items.Contains(item)) == true;

    private void PlayDiscoveryItem(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings)
    {
        if (_vm?.Playback is null || _vm.Library is null) return;
        if (item is DiscoveryItem.YouTube && !IsCurrentAccountItem(item)) return;

        var contextSnapshots = new List<TrackSnapshot>();
        TrackSnapshot? selectedSnapshot = null;

        foreach (var sib in siblings)
        {
            TrackSnapshot snap;
            if (sib is DiscoveryItem.Track tr)
            {
                snap = tr.Snapshot;
            }
            else if (sib is DiscoveryItem.YouTube { Card.PlayableVideoId: { Length: > 0 } videoId } yt)
            {
                snap = _vm.Library.UpsertFromYouTube(
                    videoId,
                    yt.Card.Title,
                    yt.Card.Uploader ?? "",
                    yt.Card.Duration ?? 0,
                    yt.Card.ThumbnailUrl);
            }
            else continue;

            contextSnapshots.Add(snap);
            if (sib == item)
            {
                selectedSnapshot = snap;
            }
        }

        if (selectedSnapshot is not null)
        {
            _vm.Playback.PlayTrack(selectedSnapshot, contextSnapshots, QueueSource.Search);
        }
    }

    private Control BuildPortraitCard(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings) =>
        BuildArtworkCard(item, siblings, AlbumCardPresentationStyle.Home, 176);

    private Control BuildArtworkCard(DiscoveryItem item, IReadOnlyList<DiscoveryItem> siblings,
        AlbumCardPresentationStyle style, double size)
    {
        var (title, uploader, artworkUrl) = ExtractItemDisplay(item);
        var card = new AlbumCardControl
        {
            CardTitle = title,
            CardSubtitle = uploader,
            ArtworkUrl = artworkUrl,
            IsYouTube = true,
            CardStyle = style,
            CardWidth = size,
            ArtworkHeight = size,
            Margin = new Avalonia.Thickness(0, 0, 18, 0)
        };
        if (CanPlay(item))
        {
            card.CardSelected += (_, _) => PlayDiscoveryItem(item, siblings);
            card.PlayRequested += (_, _) => PlayDiscoveryItem(item, siblings);
            EnableKeyboardAction(card, title, () =>
            {
                PlayDiscoveryItem(item, siblings);
                return Task.CompletedTask;
            });
        }
        else
        {
            card.Classes.Add("browseOnly");
            if (item is DiscoveryItem.YouTube yt && YouTubeMusicPage(yt.Card) is { } page)
            {
                card.CardSelected += async (_, _) => await OpenYouTubeMusicAsync(item, page);
                ToolTip.SetTip(card, L10n.Tr("Open in YouTube Music", "在 YouTube Music 中打开", "在 YouTube Music 中開啟"));
                EnableKeyboardAction(card, title, () => OpenYouTubeMusicAsync(item, page));
            }
            else
            {
                card.Cursor = new Cursor(StandardCursorType.Arrow);
            }
        }
        return card;
    }

    private static void EnableKeyboardAction(Control control, string title, Func<Task> activate)
    {
        control.Focusable = true;
        AutomationProperties.SetName(control, title);
        control.KeyUp += async (_, e) =>
        {
            if (e.Source != control || e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            await activate();
        };
    }

    private async Task OpenYouTubeMusicAsync(DiscoveryItem item, Uri page)
    {
        if (!IsCurrentAccountItem(item) || TopLevel.GetTopLevel(this) is not { } window) return;
        try
        {
            if (await window.Launcher.LaunchUriAsync(page)) return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The desktop can reject external URI launches.
        }
        if (IsCurrentAccountItem(item))
            HomeSourceStatus.Text = L10n.Tr("Could not open YouTube Music.", "无法打开 YouTube Music。", "無法開啟 YouTube Music。");
    }

    private static Uri? YouTubeMusicPage(YouTubeDiscoveryCard card)
    {
        if (card.Availability != HomeCardAvailability.Available ||
            (card.BrowseEndpoint ?? card.PlayEndpoint) is not { Kind: not HomeCardEndpointKind.Video } endpoint)
            return null;

        var id = endpoint.Identifier;
        if (id.Length is < 1 or > 128 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            return null;

        var path = endpoint.Kind switch
        {
            HomeCardEndpointKind.Playlist => $"playlist?list={id}",
            HomeCardEndpointKind.Browse => $"browse/{id}",
            HomeCardEndpointKind.Channel => $"channel/{id}",
            _ => null
        };
        return path is null ? null : new Uri($"https://music.youtube.com/{path}");
    }
}
