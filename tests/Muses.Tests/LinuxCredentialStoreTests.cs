using System.Security.Cryptography;
using System.Text;
using Muses.Platform.Linux.Credentials;
using Xunit;

namespace Muses.Tests;

public sealed class LinuxCredentialStoreTests
{
    [SecretServiceFact("available")]
    public void Linux_store_round_trips_binary_credentials_and_keeps_accounts_separate()
    {
        var store = new SecretServiceCredentialStore();
        var first = $"test-{Guid.NewGuid()}";
        var second = $"test-{Guid.NewGuid()}";
        byte[] binary = [0, 1, 2, 255, 0];

        try
        {
            Assert.Null(store.Get(first));
            Assert.True(store.Set(first, binary));
            Assert.True(store.Set(second, []));

            var reopened = new SecretServiceCredentialStore();
            Assert.Equal(binary, reopened.Get(first));
            Assert.Empty(Assert.IsType<byte[]>(reopened.Get(second)));

            Assert.True(reopened.Set(first, [9]));
            Assert.Equal(new byte[] { 9 }, store.Get(first));
            Assert.True(store.Delete(first));
            Assert.True(store.Delete(first));
            Assert.Null(reopened.Get(first));
            Assert.NotNull(reopened.Get(second));
        }
        finally
        {
            store.Delete(first);
            store.Delete(second);
        }
    }

    [SecretServiceFact("available")]
    public void Linux_store_removes_legacy_file_after_import()
    {
        var account = $"test-{Guid.NewGuid()}";
        var legacy = Directory.CreateTempSubdirectory("muses-credential-test-");
        var file = LegacyPath(legacy, account);
        byte[] original = [12, 0, 19];
        File.WriteAllBytes(file, original);
        var store = new SecretServiceCredentialStore(legacy.FullName);

        try
        {
            Assert.Equal(original, store.Get(account));
            Assert.False(File.Exists(file));
            Assert.Equal(original, new SecretServiceCredentialStore().Get(account));
        }
        finally
        {
            store.Delete(account);
            legacy.Delete(recursive: true);
        }
    }

    [SecretServiceFact("unavailable")]
    public void Linux_store_reports_missing_service_without_writing_or_using_legacy_credentials()
    {
        var account = $"test-{Guid.NewGuid()}";
        var legacy = Directory.CreateTempSubdirectory("muses-credential-test-");
        var file = LegacyPath(legacy, account);
        byte[] original = [12, 0, 19];
        File.WriteAllBytes(file, original);
        var store = new SecretServiceCredentialStore(legacy.FullName);

        try
        {
            Assert.Null(store.Get(account));
            Assert.False(store.Set(account, [7]));
            Assert.Equal(original, File.ReadAllBytes(file));
            Assert.Single(legacy.EnumerateFiles());
            Assert.False(store.Delete(account));
            Assert.Empty(legacy.EnumerateFiles());
        }
        finally
        {
            legacy.Delete(recursive: true);
        }
    }

    private static string LegacyPath(DirectoryInfo directory, string account)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)));
        return Path.Combine(directory.FullName, key[..32] + ".bin");
    }

    private sealed class SecretServiceFactAttribute : FactAttribute
    {
        public SecretServiceFactAttribute(string mode)
        {
            if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("MUSES_TEST_SECRET_SERVICE") != mode)
                Skip = $"Requires an isolated Linux session bus with Secret Service {mode}.";
        }
    }
}
