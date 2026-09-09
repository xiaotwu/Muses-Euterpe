using System.Runtime.InteropServices;
using System.Text;
using Muses.Core.Platform;

namespace Muses.Infrastructure.Platform;

/// <summary>
/// OS credential locker. On Windows uses Credential Manager (GENERIC).
/// On macOS/Linux falls back to a permission-restricted file under user config
/// only when Win32 Credential Manager is unavailable — never plaintext JSON in the Muses app-data folder on Windows.
/// </summary>
public sealed class PlatformCredentialStore : ICredentialStore
{
    private const string TargetPrefix = "Muses/YouTubeOAuth/";
    private readonly ICredentialStore _fallback;

    public PlatformCredentialStore(ICredentialStore? fallback = null)
    {
        _fallback = fallback ?? new MemoryCredentialStore();
    }

    public static ICredentialStore CreateDefault()
    {
        if (OperatingSystem.IsWindows())
            return new PlatformCredentialStore(new ProtectedFileCredentialStore());
        // Dev hosts (macOS/Linux): use protected file under XDG/config, not app sqlite.
        return new ProtectedFileCredentialStore();
    }

    public byte[]? Get(string account)
    {
        if (OperatingSystem.IsWindows() && TryWinGet(account, out var data))
            return data;
        return _fallback.Get(account);
    }

    public bool Set(string account, byte[] data)
    {
        if (OperatingSystem.IsWindows() && TryWinSet(account, data))
            return true;
        return _fallback.Set(account, data);
    }

    public bool Delete(string account)
    {
        var win = OperatingSystem.IsWindows() && TryWinDelete(account);
        var fb = _fallback.Delete(account);
        return win || fb;
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
            return CredDelete(TargetPrefix + account, 1, 0);
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
/// Restricted file under ~/.config/muses/credentials (Unix) or LocalApplicationData/Muses/credentials (Windows fallback).
/// Not used as the primary Windows path when Credential Manager succeeds.
/// </summary>
public sealed class ProtectedFileCredentialStore : ICredentialStore
{
    private readonly string _root;

    public ProtectedFileCredentialStore(string? root = null)
    {
        _root = root ?? DefaultRoot();
        Directory.CreateDirectory(_root);
    }

    private static string DefaultRoot()
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

    public byte[]? Get(string account)
    {
        var path = PathFor(account);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public bool Set(string account, byte[] data)
    {
        try
        {
            var path = PathFor(account);
            File.WriteAllBytes(path, data);
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { /* ignore */ }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Delete(string account)
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
