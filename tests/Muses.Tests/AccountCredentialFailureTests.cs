using Muses.Core.Account;
using Muses.Core.Platform;
using Muses.Infrastructure.Account;
using Muses.Infrastructure.Platform;
using Xunit;

namespace Muses.Tests;

public sealed class AccountCredentialFailureTests
{
    [Theory]
    [InlineData(YouTubeAccountService.TokensAccount)]
    [InlineData(YouTubeAccountService.ProfileAccount)]
    public void SignOut_remains_retryable_when_the_credential_store_cannot_delete_an_item(string failedAccount)
    {
        var credentials = new CredentialStoreWithFailedDelete { FailedAccount = failedAccount };
        var service = new YouTubeAccountService(credentials);
        service.SetSignedIn("test-channel", "Test Channel", null, null);
        credentials.Set(YouTubeAccountService.TokensAccount, [1, 2, 3]);

        service.SignOut();

        Assert.Equal(YouTubeAccountState.SignedIn, service.State);
        Assert.NotNull(service.ErrorMessage);
        Assert.Equal(failedAccount == YouTubeAccountService.ProfileAccount,
            credentials.Get(YouTubeAccountService.ProfileAccount) is not null);
        Assert.Equal(failedAccount == YouTubeAccountService.TokensAccount,
            credentials.Get(YouTubeAccountService.TokensAccount) is not null);

        credentials.FailedAccount = null;
        service.SignOut();

        Assert.Equal(YouTubeAccountState.SignedOut, service.State);
        Assert.Null(service.ErrorMessage);
        Assert.Null(credentials.Get(YouTubeAccountService.TokensAccount));
        Assert.Null(credentials.Get(YouTubeAccountService.ProfileAccount));
    }

    private sealed class CredentialStoreWithFailedDelete : ICredentialStore
    {
        private readonly MemoryCredentialStore _inner = new();
        public string? FailedAccount { get; set; }

        public byte[]? Get(string account) => _inner.Get(account);
        public bool Set(string account, byte[] data) => _inner.Set(account, data);
        public bool Delete(string account) => account != FailedAccount && _inner.Delete(account);
    }
}
