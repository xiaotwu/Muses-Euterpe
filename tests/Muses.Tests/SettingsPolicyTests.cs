using Muses.Core.Account;
using Muses.Infrastructure.Account;
using Muses.Infrastructure.Platform;
using Xunit;

namespace Muses.Tests;

public class SettingsPolicyTests
{
    [Fact]
    public void YouTubeAccountService_SignInAndSignOut_UpdatesState()
    {
        var service = new YouTubeAccountService();
        Assert.Equal(YouTubeAccountState.SignedOut, service.State);
        Assert.False(service.IsSignedIn);

        service.SetSignedIn("UC12345", "Test Channel", "https://example.com/avatar.jpg", "@testchannel");
        Assert.Equal(YouTubeAccountState.SignedIn, service.State);
        Assert.True(service.IsSignedIn);
        Assert.Equal("Test Channel", service.Profile?.DisplayName);
        Assert.Equal("UC12345", service.Profile?.ChannelId);

        service.SignOut();
        Assert.Equal(YouTubeAccountState.SignedOut, service.State);
        Assert.False(service.IsSignedIn);
        Assert.Null(service.Profile);
    }

    [Fact]
    public void PlatformSMTCService_EventTriggers_InvokeSubscribers()
    {
        var smtc = new PlatformSMTCService();
        var playCalled = false;
        var nextCalled = false;

        smtc.PlayRequested += () => playCalled = true;
        smtc.NextRequested += () => nextCalled = true;

        smtc.RaisePlay();
        smtc.RaiseNext();

        Assert.True(playCalled);
        Assert.True(nextCalled);
    }
}
