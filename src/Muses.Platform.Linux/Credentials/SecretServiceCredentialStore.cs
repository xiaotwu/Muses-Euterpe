using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Muses.Core.L10n;
using Muses.Core.Platform;

namespace Muses.Platform.Linux.Credentials;

/// <summary>Stores Muses account secrets in the desktop's default Secret Service collection.</summary>
public sealed class SecretServiceCredentialStore(string? legacyRoot = null) : ICredentialStore
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();

    public byte[]? Get(string account)
    {
        var accountKey = AccountKey(account);
        lock (_gate)
        {
            var result = Call(accountKey, ReadTimeout, ReadSecret);
            if (!result.Available)
                return null;
            if (result.Data is null)
                return MigrateLegacy(accountKey);

            DeleteLegacy(accountKey);
            return result.Data;
        }
    }

    public bool Set(string account, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var accountKey = AccountKey(account);
        lock (_gate)
        {
            if (!Store(accountKey, data, WriteTimeout))
                return false;

            DeleteLegacy(accountKey);
            return true;
        }
    }

    public bool Delete(string account)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        var accountKey = AccountKey(account);
        lock (_gate)
        {
            // Remove the legacy copy first so a later read cannot restore a deleted item.
            if (!DeleteLegacy(accountKey))
                return false;

            return Call(accountKey, ReadTimeout, DeleteItems);
        }
    }

    private static (bool Available, byte[]? Data) ReadSecret(IntPtr attributes, IntPtr cancellable)
    {
        // Load secrets from unlocked collections so restoring an account can run without an unlock prompt.
        var items = Search(attributes, SearchFlags.LoadSecrets, cancellable, out var error);
        try
        {
            if (error != IntPtr.Zero)
                return (false, null);
            if (items == IntPtr.Zero)
                return (true, null);

            var value = secret_item_get_secret(Marshal.ReadIntPtr(items));
            if (value == IntPtr.Zero)
                return (false, null);

            try
            {
                var data = secret_value_get(value, out var length);
                var bytes = new byte[checked((int)length)];
                if (bytes.Length > 0)
                    Marshal.Copy(data, bytes, 0, bytes.Length);
                return (true, bytes);
            }
            finally
            {
                secret_value_unref(value);
            }
        }
        finally
        {
            ReleaseSearch(items, error);
        }
    }

    private static bool DeleteItems(IntPtr attributes, IntPtr cancellable)
    {
        var items = Search(attributes, SearchFlags.All, cancellable, out var error);
        try
        {
            if (error != IntPtr.Zero)
                return false;

            for (var entry = items; entry != IntPtr.Zero;)
            {
                var link = Marshal.PtrToStructure<GList>(entry);
                if (secret_item_get_locked(link.Data) != 0)
                    return false;

                var deleted = secret_item_delete_sync(link.Data, cancellable, out var deleteError);
                FreeError(deleteError);
                if (deleted == 0)
                    return false;

                entry = link.Next;
            }

            return true;
        }
        finally
        {
            ReleaseSearch(items, error);
        }
    }

    private byte[]? MigrateLegacy(string accountKey)
    {
        if (legacyRoot is null)
            return null;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(LegacyPath(accountKey));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (Store(accountKey, bytes, ReadTimeout))
        {
            DeleteLegacy(accountKey);
            return bytes;
        }

        CryptographicOperations.ZeroMemory(bytes);
        return null;
    }

    private bool DeleteLegacy(string accountKey)
    {
        if (legacyRoot is null)
            return true;

        try
        {
            File.Delete(LegacyPath(accountKey));
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string LegacyPath(string accountKey) => Path.Combine(legacyRoot!, accountKey[..32] + ".bin");

    private static string AccountKey(string account) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)));

    private static bool Store(string accountKey, byte[] data, TimeSpan timeout) =>
        Call(accountKey, timeout, (attributes, cancellable) =>
        {
            // libsecret copies the bytes into its own SecretValue before the call returns.
            var value = secret_value_new(data.Length == 0 ? [0] : data, data.Length, "application/octet-stream");
            try
            {
                var label = L10n.Tr("Muses YouTube account", "Muses YouTube 账号");
                var stored = secret_service_store_sync(
                    IntPtr.Zero, IntPtr.Zero, attributes, IntPtr.Zero, label, value, cancellable, out var error);
                FreeError(error);
                return stored != 0;
            }
            finally
            {
                secret_value_unref(value);
            }
        });

    private static T Call<T>(string accountKey, TimeSpan timeout, Func<IntPtr, IntPtr, T> operation)
    {
        if (!OperatingSystem.IsLinux())
            return default!;

        try
        {
            var glib = GlibFunctions.Shared.Value;
            var attributes = g_hash_table_new_full(glib.StrHash, glib.StrEqual, glib.Free, glib.Free);
            try
            {
                g_hash_table_insert(attributes, g_strdup("xdg:schema"), g_strdup("io.github.xiaotwu.Muses.YouTubeOAuth"));
                g_hash_table_insert(attributes, g_strdup("account"), g_strdup(accountKey));

                var cancellable = g_cancellable_new();
                try
                {
                    using var timer = new CancellationTokenSource(timeout);
                    using var registration = timer.Token.Register(() => g_cancellable_cancel(cancellable));
                    return operation(attributes, cancellable);
                }
                finally
                {
                    g_object_unref(cancellable);
                }
            }
            finally
            {
                g_hash_table_unref(attributes);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return default!;
        }
    }

    private static IntPtr Search(IntPtr attributes, SearchFlags flags, IntPtr cancellable, out IntPtr error) =>
        secret_service_search_sync(IntPtr.Zero, IntPtr.Zero, attributes, flags, cancellable, out error);

    private static void ReleaseSearch(IntPtr items, IntPtr error)
    {
        if (items != IntPtr.Zero)
            g_list_free_full(items, GlibFunctions.Shared.Value.ObjectUnref);
        FreeError(error);
    }

    private static void FreeError(IntPtr error)
    {
        if (error != IntPtr.Zero)
            g_error_free(error);
    }

    [Flags]
    private enum SearchFlags
    {
        All = 2,
        LoadSecrets = 8
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GList
    {
        public IntPtr Data;
        public IntPtr Next;
        public IntPtr Previous;
    }

    private const string LibSecret = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";

    private sealed class GlibFunctions
    {
        // Keep native libraries loaded while the hash tables and lists can invoke their callbacks.
        public static readonly Lazy<GlibFunctions> Shared = new(() => new GlibFunctions());
        private readonly IntPtr _glib = NativeLibrary.Load(GLib);
        private readonly IntPtr _gobject = NativeLibrary.Load(GObject);
        public IntPtr StrHash => NativeLibrary.GetExport(_glib, "g_str_hash");
        public IntPtr StrEqual => NativeLibrary.GetExport(_glib, "g_str_equal");
        public IntPtr Free => NativeLibrary.GetExport(_glib, "g_free");
        public IntPtr ObjectUnref => NativeLibrary.GetExport(_gobject, "g_object_unref");
    }

    [DllImport(LibSecret)]
    private static extern IntPtr secret_service_search_sync(
        IntPtr service, IntPtr schema, IntPtr attributes, SearchFlags flags, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_service_store_sync(
        IntPtr service, IntPtr schema, IntPtr attributes, IntPtr collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label, IntPtr value, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_item_get_locked(IntPtr item);

    [DllImport(LibSecret)]
    private static extern IntPtr secret_item_get_secret(IntPtr item);

    [DllImport(LibSecret)]
    private static extern int secret_item_delete_sync(IntPtr item, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern IntPtr secret_value_new(
        [In] byte[] data, nint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);

    [DllImport(LibSecret)]
    private static extern IntPtr secret_value_get(IntPtr value, out nuint length);

    [DllImport(LibSecret)]
    private static extern void secret_value_unref(IntPtr value);

    [DllImport(GLib)]
    private static extern IntPtr g_hash_table_new_full(
        IntPtr hashFunc, IntPtr equalFunc, IntPtr keyDestroy, IntPtr valueDestroy);

    [DllImport(GLib)]
    private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GLib)]
    private static extern void g_hash_table_unref(IntPtr table);

    [DllImport(GLib)]
    private static extern IntPtr g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(GLib)]
    private static extern void g_list_free_full(IntPtr list, IntPtr destroy);

    [DllImport(GLib)]
    private static extern void g_error_free(IntPtr error);

    [DllImport(GObject)]
    private static extern void g_object_unref(IntPtr value);

    [DllImport(Gio)]
    private static extern IntPtr g_cancellable_new();

    [DllImport(Gio)]
    private static extern void g_cancellable_cancel(IntPtr cancellable);
}
