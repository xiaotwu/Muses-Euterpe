using System.Diagnostics;
using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Infrastructure.Playback;
using Muses.Platform.Linux.Media;
using Tmds.DBus.Protocol;
using Xunit;

namespace Muses.Tests;

public sealed class LinuxMprisTests
{
    private const string BusName = LinuxMprisService.BusName;
    private const string Path = "/org/mpris/MediaPlayer2";
    private const string Player = "org.mpris.MediaPlayer2.Player";
    private const string Properties = "org.freedesktop.DBus.Properties";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [LinuxDbusFact]
    public async Task Private_bus_exports_metadata_controls_properties_and_seek_signal()
    {
        using var daemon = Process.Start(new ProcessStartInfo("/usr/bin/dbus-daemon")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            ArgumentList = { "--session", "--nofork", "--print-address=1" }
        })!;

        try
        {
            var address = await daemon.StandardOutput.ReadLineAsync().WaitAsync(Timeout);
            Assert.False(string.IsNullOrWhiteSpace(address));
            var engine = new FakePlayerEngine();
            var playback = new PlaybackService(engine, new QueueService());
            var first = TrackSnapshot.Test("First", durationSeconds: 120) with
            {
                AlbumTitle = "Test album",
                ArtworkUrl = "https://example.com/cover.jpg"
            };
            var second = TrackSnapshot.Test("Second", durationSeconds: 100);
            var dispatchLock = new object();
            Task Dispatch(Action action)
            {
                lock (dispatchLock) action();
                return Task.CompletedTask;
            }

            var raised = false;
            var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            LinuxMprisService? activeService = null;
            using var service = new LinuxMprisService(playback, Dispatch, () => raised = true, () =>
            {
                activeService?.Dispose();
                quit.TrySetResult();
            }, address);
            activeService = service;
            await service.StartAsync();
            Assert.True(service.IsSupported);
            using var client = new DBusConnection(address!);
            await client.ConnectAsync().AsTask().WaitAsync(Timeout);
            Assert.Contains(BusName, await client.ListServicesAsync().WaitAsync(Timeout));
            var xml = await client.CallMethodAsync(Request("org.freedesktop.DBus.Introspectable", "Introspect"),
                static (message, _) => message.GetBodyReader().ReadString()).WaitAsync(Timeout);
            Assert.Contains("org.freedesktop.DBus.Properties", xml);
            Assert.Contains("org.mpris.MediaPlayer2.Player", xml);
            await client.CallMethodAsync(Request("org.mpris.MediaPlayer2", "Raise")).WaitAsync(Timeout);
            Assert.True(raised);

            Assert.Equal("Stopped", (await Get("PlaybackStatus")).GetString());
            Assert.False((await Get("CanPlay")).GetBool());

            var changes = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnStatus(Notification<Dictionary<string, VariantValue>> notification)
            {
                if (notification.HasValue && notification.Value.TryGetValue("PlaybackStatus", out var status)
                    && status.GetString() == "Playing")
                    changes.TrySetResult(status.GetString());
            }

            using var statusWatch = await client.WatchSignalAsync(
                BusName, Path, Properties, "PropertiesChanged", ReadPropertiesChanged, OnStatus,
                flags: ObserverFlags.None, emitOnCapturedContext: false);
            await Dispatch(() => playback.PlayTrack(first, [first, second], QueueSource.Album));
            Assert.Equal("Playing", await changes.Task.WaitAsync(Timeout));
            var metadata = (await Get("Metadata")).GetDictionary<string, VariantValue>();
            Assert.Equal("First", metadata["xesam:title"].GetString());
            Assert.Equal(new[] { "a" }, metadata["xesam:artist"].GetArray<string>());
            Assert.Equal("Test album", metadata["xesam:album"].GetString());
            Assert.Equal("https://example.com/cover.jpg", metadata["mpris:artUrl"].GetString());
            Assert.Equal(120_000_000L, metadata["mpris:length"].GetInt64());
            var firstPath = metadata["mpris:trackid"].GetObjectPath();

            var seeked = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnSeeked(Notification<long> notification)
            {
                if (notification.HasValue) seeked.TrySetResult(notification.Value);
            }

            using var seekWatch = await client.WatchSignalAsync(
                BusName, Path, Player, "Seeked", static (message, _) => message.GetBodyReader().ReadInt64(),
                OnSeeked, flags: ObserverFlags.None, emitOnCapturedContext: false);
            await client.CallMethodAsync(Request(Player, "SetPosition", "ox", (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(firstPath);
                writer.WriteInt64(12_500_000);
            })).WaitAsync(Timeout);
            Assert.Equal(12_500_000L, await seeked.Task.WaitAsync(Timeout));
            Assert.Equal(12_500_000L, (await Get("Position")).GetInt64());
            await client.CallMethodAsync(Request(Player, "Seek", "x",
                (ref MessageWriter writer) => writer.WriteInt64(long.MinValue))).WaitAsync(Timeout);
            Assert.Equal(0L, (await Get("Position")).GetInt64());

            await Call("Pause");
            Assert.Equal("Paused", (await Get("PlaybackStatus")).GetString());
            await Call("Next");
            Assert.Equal(second.Id, engine.State.Track?.Id);
            Assert.Equal("Paused", (await Get("PlaybackStatus")).GetString());
            await client.CallMethodAsync(Request(Player, "SetPosition", "ox", (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(firstPath);
                writer.WriteInt64(5_000_000);
            })).WaitAsync(Timeout);
            Assert.Equal(0L, (await Get("Position")).GetInt64());
            await Call("Previous");
            Assert.Equal(first.Id, engine.State.Track?.Id);
            await Call("PlayPause");
            Assert.Equal("Playing", (await Get("PlaybackStatus")).GetString());

            await Set("LoopStatus", "Playlist");
            Assert.Equal(RepeatMode.All, playback.Queue.RepeatMode);
            await Set("Shuffle", true);
            Assert.True(playback.Queue.Shuffle);
            await Set("Volume", 0.5);
            Assert.Equal(0.5f, engine.VolumeSet);
            var readOnly = await Assert.ThrowsAsync<DBusErrorReplyException>(() => Set("Position", 9L));
            Assert.Equal("org.freedesktop.DBus.Error.PropertyReadOnly", readOnly.ErrorName);

            await Call("Stop");
            Assert.Equal("Stopped", (await Get("PlaybackStatus")).GetString());
            Assert.Equal(0L, (await Get("Position")).GetInt64());
            await Call("Play");
            Assert.Equal("Playing", (await Get("PlaybackStatus")).GetString());
            await client.CallMethodAsync(Request("org.mpris.MediaPlayer2", "Quit")).WaitAsync(Timeout);
            await quit.Task.WaitAsync(Timeout);
            Assert.False(service.IsSupported);

            Task Call(string member) => client.CallMethodAsync(Request(Player, member)).WaitAsync(Timeout);

            Task<VariantValue> Get(string property) => client.CallMethodAsync(
                Request(Properties, "Get", "ss", (ref MessageWriter writer) =>
                {
                    writer.WriteString(Player);
                    writer.WriteString(property);
                }), static (message, _) => message.GetBodyReader().ReadVariantValue()).WaitAsync(Timeout);

            Task Set(string property, VariantValue value) => client.CallMethodAsync(
                Request(Properties, "Set", "ssv", (ref MessageWriter writer) =>
                {
                    writer.WriteString(Player);
                    writer.WriteString(property);
                    writer.WriteVariant(value);
                })).WaitAsync(Timeout);

            MessageBuffer Request(string iface, string member, string? signature = null, WriteBody? body = null)
            {
                var writer = client.GetMessageWriter();
                try
                {
                    writer.WriteMethodCallHeader(destination: BusName, path: Path, @interface: iface, member: member, signature: signature);
                    body?.Invoke(ref writer);
                    return writer.CreateMessage();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }
        finally
        {
            if (!daemon.HasExited) daemon.Kill();
            await daemon.WaitForExitAsync().WaitAsync(Timeout);
        }
    }

    private static Dictionary<string, VariantValue> ReadPropertiesChanged(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return reader.ReadString() == Player ? reader.ReadDictionaryOfStringToVariantValue() : [];
    }

    private delegate void WriteBody(ref MessageWriter writer);

    private sealed class LinuxDbusFactAttribute : FactAttribute
    {
        public LinuxDbusFactAttribute()
        {
            if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/dbus-daemon"))
                Skip = "Requires a Linux host with dbus-daemon.";
        }
    }
}
