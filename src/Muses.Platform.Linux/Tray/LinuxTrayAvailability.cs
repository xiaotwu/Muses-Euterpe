using System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace Muses.Platform.Linux.Tray;

public static class LinuxTrayAvailability
{
    private const string Watcher = "org.kde.StatusNotifierWatcher";

    public static async Task<bool> IsRegisteredAsync()
    {
        try
        {
            var address = DBusAddress.Session;
            if (address is null)
                return false;

            using var connection = new DBusConnection(address);
            return await ReadRegistrationAsync(connection)
                .WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or SocketException or TimeoutException
            or IOException or ArgumentException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> ReadRegistrationAsync(DBusConnection connection)
    {
        await connection.ConnectAsync().ConfigureAwait(false);
        return await QueryWatcherAsync(connection).ConfigureAwait(false);
    }

    private static Task<bool> QueryWatcherAsync(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Watcher, path: "/StatusNotifierWatcher",
            @interface: "org.freedesktop.DBus.Properties", member: "GetAll", signature: "s",
            flags: MessageFlags.NoAutoStart);
        writer.WriteString(Watcher);
        return connection.CallMethodAsync(writer.CreateMessage(), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            var properties = reader.ReadDictionaryOfStringToVariantValue();
            if (!properties.TryGetValue("IsStatusNotifierHostRegistered", out var host) || !host.GetBool()
                || !properties.TryGetValue("RegisteredStatusNotifierItems", out var items))
                return false;

            // Avalonia's X11 tray backend exports each icon under a per-process StatusNotifierItem name.
            var prefix = FormattableString.Invariant($"org.kde.StatusNotifierItem-{Environment.ProcessId}-");
            return items.GetArray<string>().Any(item => item.StartsWith(prefix, StringComparison.Ordinal));
        });
    }
}
