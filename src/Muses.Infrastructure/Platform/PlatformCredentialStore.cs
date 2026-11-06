using System.Runtime.InteropServices;
using System.Text;
using Muses.Core.Platform;
using Muses.Platform.Linux.Credentials;

namespace Muses.Infrastructure.Platform;

/// <summary>
/// Windows Credential Manager adapter and platform credential store factory.
/// </summary>
public sealed class PlatformCredentialStore : ICredentialStore
{
    private const string TargetPrefix = "Muses/YouTubeOAuth/";
    private const int ErrorNotFound = 1168;
    private readonly LegacyCredentialFiles _legacy;

    internal PlatformCredentialStore(string? legacyRoot = null) => _legacy = new LegacyCredentialFiles(legacyRoot);

    public static ICredentialStore CreateDefault()
    {
        if (OperatingSystem.IsWindows())
            return new PlatformCredentialStore();
        if (OperatingSystem.IsLinux())
            return new SecretServiceCredentialStore(LegacyCredentialFiles.DefaultRoot());

        return new MemoryCredentialStore();
    }

    public byte[]? Get(string account)
    {
        if (TryWinGet(account, out var data))
            return data;
        var legacyData = _legacy.Get(account);
        return legacyData is not null && Set(account, legacyData) ? legacyData : null;
    }

    public bool Set(string account, byte[] data)
    {
        if (!TryWinSet(account, data)) return false;
        _legacy.Delete(account);
        return true;
    }

    public bool Delete(string account)
    {
        var win = TryWinDelete(account);
        var legacy = _legacy.Delete(account);
        return win && legacy;
    }

    private static bool TryWinGet(string account, out byte[]? data)
    {
        data = null;
        try
        {
            if (!CredRead(TargetPrefix + account, 1 /*GENERIC*/, 0, out var ptr) || ptr == IntPtr.Zero)
                return false;
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero)
                    return false;
                data = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, data, 0, data.Length);
                return true;
            }
            finally
            {
                CredFree(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWinSet(string account, byte[] data)
    {
        try
        {
            var blob = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, blob, data.Length);
                var cred = new CREDENTIAL
                {
                    Type = 1,
                    TargetName = TargetPrefix + account,
                    CredentialBlobSize = data.Length,
                    CredentialBlob = blob,
                    Persist = 2, // LocalMachine
                    UserName = "Muses"
                };
                return CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWinDelete(string account)
    {
        try
        {
            return CredDelete(TargetPrefix + account, 1, 0) || Marshal.GetLastWin32Error() == ErrorNotFound;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int reservedFlag);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>
/// Legacy file location retained for one-way migration to the host credential store.
/// </summary>
internal sealed class LegacyCredentialFiles
{
    private readonly string _root;

    internal LegacyCredentialFiles(string? root = null) => _root = root ?? DefaultRoot();

    internal static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Muses", "credentials");
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "muses", "credentials");
    }

    private string PathFor(string account)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(account)))[..32];
        return Path.Combine(_root, safe + ".bin");
    }

    internal byte[]? Get(string account)
    {
        try
        {
            var path = PathFor(account);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    internal bool Delete(string account)
    {
        try
        {
            var path = PathFor(account);
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
