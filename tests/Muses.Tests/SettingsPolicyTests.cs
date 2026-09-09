using Muses.Core.Account;
using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Preferences;
using Muses.Core.Queue;
using Muses.Infrastructure.Account;
using Muses.Infrastructure.Playback;
using Muses.Infrastructure.Platform;
using Muses.Persistence;
using Muses.Platform.Windows;
using Xunit;

namespace Muses.Tests;

public class SettingsPolicyTests
{
    [Fact]
    public void Preferences_round_trip_survives_new_store_handle()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var prefs = new SqlitePreferences(store);
        prefs.SetDouble(PrefKey.Volume, 0.42);
        prefs.SetBool(PrefKey.ResumeAfterVideo, false);
        prefs.SetBool(PrefKey.ReplayGainEnabled, true);
        prefs.SetBool(PrefKey.SidebarCollapsed, true);
        prefs.SetBool(PrefKey.CloseToTray, false);
        prefs.SetBool(PrefKey.FfTray, true);
        prefs.SetBool(PrefKey.WebHomeEnabled, false);
        prefs.SetString(PrefKey.WebHomeConsentVersion, "2");
        prefs.SetString(PrefKey.Theme, "system");
        prefs.SetString(PrefKey.Language, "zh");
        prefs.SetString(PrefKey.AudioQuality, "high");

        var again = new SqlitePreferences(store);
        Assert.Equal(0.42, again.GetDouble(PrefKey.Volume, 0.8), 3);
        Assert.False(again.GetBool(PrefKey.ResumeAfterVideo, true));
        Assert.True(again.GetBool(PrefKey.ReplayGainEnabled, false));
        Assert.True(again.GetBool(PrefKey.SidebarCollapsed, false));
        Assert.False(again.GetBool(PrefKey.CloseToTray, true));
        Assert.True(again.GetBool(PrefKey.FfTray, false));
        Assert.False(again.GetBool(PrefKey.WebHomeEnabled, true));
        Assert.Equal("2", again.GetString(PrefKey.WebHomeConsentVersion, "0"));
        Assert.Equal("system", again.GetString(PrefKey.Theme, "dark"));
        Assert.Equal("zh", again.GetString(PrefKey.Language, "system"));
        Assert.Equal("high", again.GetString(PrefKey.AudioQuality, "best"));
    }

    [Fact]
    public async Task YouTubeAccount_missing_client_id_sets_Error_not_fake_user()
    {
        var creds = new MemoryCredentialStore();
        var service = new YouTubeAccountService(
            credentials: creds,
            configProvider: () => null,
            allowLiveNetwork: false);

        await service.StartGoogleSignInAsync();

        Assert.Equal(YouTubeAccountState.Error, service.State);
        Assert.False(service.IsSignedIn);
        Assert.Null(service.Profile);
        Assert.NotNull(service.ErrorMessage);
        Assert.DoesNotContain("Muses User", service.ErrorMessage);
        Assert.Null(creds.Get(YouTubeAccountService.TokensAccount));
    }

    [Fact]
    public void YouTubeAccount_SignOut_clears_credential_locker()
    {
        var creds = new MemoryCredentialStore();
        var service = new YouTubeAccountService(credentials: creds, configProvider: () => null, allowLiveNetwork: false);
        service.SetSignedIn("UC_test", "Real Channel", null, "@real");
        Assert.NotNull(creds.Get(YouTubeAccountService.ProfileAccount));

        // Simulate tokens present
        creds.Set(YouTubeAccountService.TokensAccount, """{"AccessToken":"x","RefreshToken":"y","ExpiresAt":"2099-01-01T00:00:00+00:00"}"""u8.ToArray());

        service.SignOut();
        Assert.Equal(YouTubeAccountState.SignedOut, service.State);
        Assert.Null(service.Profile);
        Assert.Null(creds.Get(YouTubeAccountService.TokensAccount));
        Assert.Null(creds.Get(YouTubeAccountService.ProfileAccount));
    }

    [Fact]
    public void GoogleOAuthConfig_FromEnvironment_requires_client_id()
    {
        Assert.Null(GoogleOAuthConfig.FromEnvironment(new Dictionary<string, string>()));
        var cfg = GoogleOAuthConfig.FromEnvironment(new Dictionary<string, string>
        {
            ["MUSES_GOOGLE_OAUTH_CLIENT_ID"] = "test-client.apps.googleusercontent.com",
            ["MUSES_GOOGLE_OAUTH_REDIRECT_URI"] = "http://127.0.0.1:53682/"
        });
        Assert.NotNull(cfg);
        Assert.Equal("test-client.apps.googleusercontent.com", cfg!.ClientId);
    }

    [Fact]
    public async Task SMTC_PlayRequested_calls_PlaybackService_Play()
    {
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        var track = Muses.Core.Domain.TrackSnapshot.Test("smtc");
        playback.PlayTrack(track, [track], Muses.Core.Domain.QueueSource.Songs);
        await Task.Delay(20);
        playback.Pause();
        Assert.False(engine.State.IsPlaying);

        var smtc = new WindowsSMTCService(playback);
        smtc.RaisePlay();
        Assert.True(engine.State.IsPlaying);
        Assert.True(engine.PlayCallCount >= 1);
        smtc.Dispose();
    }

    [Fact]
    public void Feature_flag_defaults_match_chrome_contract()
    {
        var prefs = new MemoryPreferences();
        Assert.True(FeatureFlagDefaults.IsEnabled(prefs, PrefKey.FfTray));
        Assert.False(FeatureFlagDefaults.IsEnabled(prefs, PrefKey.FfMiniPlayer));
        Assert.False(FeatureFlagDefaults.IsEnabled(prefs, PrefKey.FfDesktopLyrics));
        Assert.False(FeatureFlagDefaults.IsEnabled(prefs, PrefKey.FfGlobalHotkeys));
    }

    [Fact]
    public void WindowsSMTC_IsSupported_false_on_non_windows_dev_hosts()
    {
        var smtc = new WindowsSMTCService();
        if (!OperatingSystem.IsWindows())
            Assert.False(smtc.IsSupported);
        smtc.Dispose();
    }
}
