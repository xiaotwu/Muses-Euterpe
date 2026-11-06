using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Muses.Core.Advanced;
using Muses.Core.L10n;
using Muses.Core.Preferences;
using Muses.Infrastructure.Discovery;

namespace Muses.App.ViewModels;

public partial class ShellViewModel
{
    private bool _isAccountHomeSelected;
    private string? _accountHomeChannel;

    public AccountHomeDiscoveryService? HomeDiscovery { get; private set; }
    public bool CanSelectAccountHome => HomeDiscovery is not null;
    public bool IsAccountHomeSelected
    {
        get => _isAccountHomeSelected;
        private set => SetProperty(ref _isAccountHomeSelected, value);
    }

    private string? ActiveAccountHomeChannel => IsAccountSignedIn ? Account?.Profile?.ChannelId : null;

    private void AttachAccountHome()
    {
        _accountHomeChannel = ActiveAccountHomeChannel;
        if (WebHome is { } webHome)
        {
            HomeDiscovery = new AccountHomeDiscoveryService(webHome);
            HomeDiscovery.Changed += OnWebHomeStatusChanged;
            webHome.StatusChanged += OnWebHomeStatusChanged;
            IsAccountHomeSelected = Preferences.GetString(PrefKey.HomeRecommendationMode, "recommended") == "account";
        }
        if (Account is not null)
            Account.Changed += OnYouTubeAccountChanged;

        RefreshWebHomeStatus();
        LoadAccountHome();
    }

    public void StopAccountHome()
    {
        if (Account is not null)
            Account.Changed -= OnYouTubeAccountChanged;
        if (HomeDiscovery is { } discovery)
        {
            discovery.Changed -= OnWebHomeStatusChanged;
            discovery.Clear();
        }
        if (WebHome is { } webHome)
        {
            webHome.StatusChanged -= OnWebHomeStatusChanged;
            _ = webHome.ResetAsync();
        }
    }

    public void SetHomeSource(bool account)
    {
        if ((account && !CanSelectAccountHome) || IsAccountHomeSelected == account) return;

        IsAccountHomeSelected = account;
        Preferences.SetString(PrefKey.HomeRecommendationMode, account ? "account" : "recommended");
        if (account)
            LoadAccountHome();
        else
            HomeDiscovery?.Clear();
    }

    public void RefreshAccountHome() => LoadAccountHome(forceReload: true);

    partial void OnSelectedSectionChanged(SidebarSection value) => Dispatcher.UIThread.Post(UpdateAccountHomeRoute);

    partial void OnSettingsOpenChanged(bool value) => Dispatcher.UIThread.Post(UpdateAccountHomeRoute);

    private void UpdateAccountHomeRoute()
    {
        if (IsHome && !SettingsOpen)
            LoadAccountHome();
        else if (HomeDiscovery is { IsRefreshing: true } discovery)
            discovery.Clear();
    }

    private void LoadAccountHome(bool forceReload = false)
    {
        if (IsHome && !SettingsOpen && IsAccountHomeSelected && ActiveAccountHomeChannel is not null &&
            WebHome is { IsEnabled: true, HasConsent: true } && HomeDiscovery is { } discovery)
            _ = discovery.LoadAsync(forceReload);
    }

    private void OnYouTubeAccountChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            NotifyAccountChanged();
        else
            Dispatcher.UIThread.Post(NotifyAccountChanged);
    }

    private void UpdateAccountHomeScope()
    {
        var channel = ActiveAccountHomeChannel;
        if (_accountHomeChannel == channel) return;

        _accountHomeChannel = channel;
        RevokeAccountHomeConsent();
    }

    private void RevokeAccountHomeConsent()
    {
        HomeDiscovery?.Clear();
        if (WebHome is { } webHome && (webHome.IsEnabled || webHome.HasConsent))
            _ = webHome.DisableAndClearAsync();
        RefreshWebHomeStatus();
    }

    private void OnWebHomeStatusChanged() => Dispatcher.UIThread.Post(RefreshWebHomeStatus);

    private void RefreshWebHomeStatus()
    {
        WebHomeEnabled = WebHome is { IsEnabled: true, HasConsent: true };
        WebHomeStatusText = WebHome?.Status switch
        {
            WebHomeSessionStatus.Available => L10n.Tr("Browser session matches this account.", "浏览器会话与此账号匹配。"),
            WebHomeSessionStatus.AccountMismatch => L10n.Tr("The browser is signed in to a different YouTube channel.", "浏览器登录的是其他 YouTube 频道。"),
            WebHomeSessionStatus.PendingConsent => L10n.Tr("Review browser consent to enable personalized Home.", "请查看浏览器授权说明以启用个性化首页。"),
            WebHomeSessionStatus.Checking => L10n.Tr("Checking the browser session…", "正在验证浏览器会话…"),
            WebHomeSessionStatus.Refreshing => L10n.Tr("Loading YouTube Music recommendations…", "正在加载 YouTube Music 推荐…"),
            WebHomeSessionStatus.Expired => L10n.Tr("Sign in to YouTube Music in the browser and try again.", "请在浏览器中登录 YouTube Music 后重试。"),
            WebHomeSessionStatus.ShapeChanged => L10n.Tr("YouTube Music recommendations are currently unavailable.", "YouTube Music 推荐暂时无法使用。"),
            WebHomeSessionStatus.Unavailable or WebHomeSessionStatus.DisabledByBuild or null =>
                L10n.Tr("Personalized Home is unavailable.", "个性化首页暂不可用。"),
            _ => WebHomeEnabled
                ? L10n.Tr("Personalized Home is enabled.", "个性化首页已启用。")
                : L10n.Tr("Personalized Home is off.", "个性化首页已关闭。")
        };
        OnPropertyChanged(nameof(IsWebHomeAvailable));
    }

    [RelayCommand]
    private void EnableWebHome()
    {
        if (WebHome is not { } webHome || ActiveAccountHomeChannel is null || WebHomeEnabled) return;
        webHome.EnableWithConsent();
        RefreshWebHomeStatus();
        LoadAccountHome();
    }

    [RelayCommand]
    private async Task DisableWebHomeAsync()
    {
        if (WebHome is not { } webHome) return;
        HomeDiscovery?.Clear();
        await webHome.DisableAndClearAsync();
        RefreshWebHomeStatus();
    }

    [RelayCommand]
    public async Task ProbeWebHomeAsync()
    {
        if (WebHome is not { IsEnabled: true, HasConsent: true } webHome || ActiveAccountHomeChannel is null)
            return;

        HomeDiscovery?.Clear();
        await webHome.ProbeSessionAsync();
        RefreshWebHomeStatus();
    }
}
