using System.Diagnostics;
using Muses.Core.Domain;
using Tmds.DBus.Protocol;

namespace Muses.Platform.Linux.Media;

public sealed partial class LinuxMprisService
{
    private const string ObjectPath = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = RootInterface + ".Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string ErrorPrefix = "org.freedesktop.DBus.Error.";
    private static readonly ObjectPath NoTrack = ObjectPath + "/TrackList/NoTrack";

    private static readonly ReadOnlyMemory<byte>[] Interfaces =
    [
        IntrospectionXml.DBusIntrospectable,
        IntrospectionXml.DBusProperties,
        """
        <interface name="org.mpris.MediaPlayer2">
          <method name="Raise"/>
          <method name="Quit"/>
          <property name="CanQuit" type="b" access="read"/>
          <property name="CanRaise" type="b" access="read"/>
          <property name="HasTrackList" type="b" access="read"/>
          <property name="Identity" type="s" access="read"/>
          <property name="DesktopEntry" type="s" access="read"/>
          <property name="SupportedUriSchemes" type="as" access="read"/>
          <property name="SupportedMimeTypes" type="as" access="read"/>
        </interface>
        """u8.ToArray(),
        """
        <interface name="org.mpris.MediaPlayer2.Player">
          <method name="Next"/>
          <method name="Previous"/>
          <method name="Pause"/>
          <method name="PlayPause"/>
          <method name="Stop"/>
          <method name="Play"/>
          <method name="Seek"><arg name="Offset" type="x" direction="in"/></method>
          <method name="SetPosition">
            <arg name="TrackId" type="o" direction="in"/>
            <arg name="Position" type="x" direction="in"/>
          </method>
          <method name="OpenUri"><arg name="Uri" type="s" direction="in"/></method>
          <signal name="Seeked"><arg name="Position" type="x"/></signal>
          <property name="PlaybackStatus" type="s" access="read"/>
          <property name="LoopStatus" type="s" access="readwrite"/>
          <property name="Rate" type="d" access="readwrite"/>
          <property name="Shuffle" type="b" access="readwrite"/>
          <property name="Metadata" type="a{sv}" access="read"/>
          <property name="Volume" type="d" access="readwrite"/>
          <property name="Position" type="x" access="read">
            <annotation name="org.freedesktop.DBus.Property.EmitsChangedSignal" value="false"/>
          </property>
          <property name="MinimumRate" type="d" access="read"/>
          <property name="MaximumRate" type="d" access="read"/>
          <property name="CanGoNext" type="b" access="read"/>
          <property name="CanGoPrevious" type="b" access="read"/>
          <property name="CanPlay" type="b" access="read"/>
          <property name="CanPause" type="b" access="read"/>
          <property name="CanSeek" type="b" access="read"/>
          <property name="CanControl" type="b" access="read">
            <annotation name="org.freedesktop.DBus.Property.EmitsChangedSignal" value="false"/>
          </property>
        </interface>
        """u8.ToArray()
    ];

    private sealed class Endpoint(LinuxMprisService owner) : IPathMethodHandler
    {
        public string Path => ObjectPath;
        public bool HandlesChildPaths => false;

        public async ValueTask HandleMethodAsync(MethodContext context)
        {
            try
            {
                await owner._dispatch(() => owner.HandleRequest(context)).ConfigureAwait(false);
            }
            catch (Exception error) when (error is DBusReadException or ArgumentException)
            {
                context.ReplyError(ErrorPrefix + "InvalidArgs", "Invalid MPRIS arguments.");
            }
            catch (Exception error)
            {
                Trace.TraceWarning("MPRIS method failed: {0}", error.GetType().Name);
                if (!context.ReplySent)
                    context.ReplyError(ErrorPrefix + "Failed", "MPRIS request failed.");
            }
        }
    }

    private void HandleRequest(MethodContext context)
    {
        if (_disposed || context.RequestAborted.IsCancellationRequested) return;
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(Interfaces);
            return;
        }

        var request = context.Request;
        var member = request.MemberAsString;
        var signature = request.SignatureAsString ?? "";
        if (context.IsPropertiesInterfaceRequest)
        {
            HandleProperties(context, member, signature);
            return;
        }

        if (request.InterfaceAsString == RootInterface && signature.Length == 0 && member is "Raise" or "Quit")
        {
            if (member == "Raise") _raise?.Invoke();
            Reply(context);
            if (member == "Quit" && _quit is { } quit && _connection is { } connection)
                _ = QuitAfterReplyAsync(connection, quit);
            return;
        }

        if (request.InterfaceAsString != PlayerInterface)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var state = Capture();
        switch (member, signature)
        {
            case ("Next", ""):
                if (state.CanGoNext) Move(forward: true, state.Status);
                break;
            case ("Previous", ""):
                if (state.CanGoPrevious) Move(forward: false, state.Status);
                break;
            case ("Pause", ""):
                if (state.CanPause) Pause();
                break;
            case ("Play", ""):
                if (state.CanPlay) Play();
                break;
            case ("PlayPause", ""):
                if (state.CanPlay) Toggle();
                break;
            case ("Stop", ""):
                Stop();
                break;
            case ("Seek", "x"):
                Seek(request.GetBodyReader().ReadInt64());
                break;
            case ("SetPosition", "ox"):
                var reader = request.GetBodyReader();
                SetPosition(reader.ReadObjectPath(), reader.ReadInt64());
                break;
            case ("OpenUri", "s"):
                context.ReplyError(ErrorPrefix + "NotSupported", "OpenUri is unavailable.");
                return;
            default:
                context.ReplyUnknownMethodError();
                return;
        }

        PublishChanges();
        Reply(context);
    }

    private void HandleProperties(MethodContext context, string? member, string signature)
    {
        if ((member, signature) is not (("Get", "ss") or ("GetAll", "s") or ("Set", "ssv")))
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var reader = context.Request.GetBodyReader();
        var interfaceName = reader.ReadString();
        var properties = interfaceName switch
        {
            RootInterface => RootProperties(),
            PlayerInterface => PlayerProperties(Capture()),
            _ => null
        };
        if (properties is null)
        {
            context.ReplyError(ErrorPrefix + "UnknownInterface", "Unknown MPRIS interface.");
            return;
        }

        if (member == "GetAll")
        {
            using var writer = context.CreateReplyWriter("a{sv}");
            writer.WriteDictionary(properties);
            context.Reply(writer.CreateMessage());
            return;
        }

        var property = reader.ReadString();
        if (!properties.TryGetValue(property, out var value))
        {
            context.ReplyError(ErrorPrefix + "UnknownProperty", "Unknown MPRIS property.");
            return;
        }

        if (member == "Get")
        {
            using var writer = context.CreateReplyWriter("v");
            writer.WriteVariant(value);
            context.Reply(writer.CreateMessage());
            return;
        }

        if (interfaceName != PlayerInterface || property is not ("LoopStatus" or "Rate" or "Shuffle" or "Volume"))
        {
            context.ReplyError(ErrorPrefix + "PropertyReadOnly", "MPRIS property is read-only.");
            return;
        }

        var newValue = reader.ReadVariantValue();
        if (value.Type != newValue.Type || !TrySetPlayerProperty(property, newValue))
        {
            context.ReplyError(ErrorPrefix + "InvalidArgs", "Invalid MPRIS property value.");
            return;
        }

        PublishChanges();
        Reply(context);
    }

    private bool TrySetPlayerProperty(string property, VariantValue value)
    {
        switch (property)
        {
            case "LoopStatus":
                var repeat = value.GetString() switch
                {
                    "None" => RepeatMode.Off,
                    "Track" => RepeatMode.One,
                    "Playlist" => RepeatMode.All,
                    _ => (RepeatMode?)null
                };
                if (repeat is not { } mode) return false;
                if (_playback.Queue.RepeatMode != mode) _playback.Queue.SetRepeat(mode);
                break;
            case "Rate":
                if (!double.IsFinite(value.GetDouble())) return false;
                if (value.GetDouble() == 0 && Capture().CanPause) Pause();
                break;
            case "Shuffle":
                if (_playback.Queue.Shuffle != value.GetBool()) _playback.ToggleShuffle();
                break;
            case "Volume":
                var volume = value.GetDouble();
                if (!double.IsFinite(volume)) return false;
                _playback.SetVolume((float)Math.Clamp(volume, 0, 1));
                break;
        }

        return true;
    }

    private Dictionary<string, VariantValue> RootProperties() => new()
    {
        ["CanQuit"] = _quit is not null,
        ["CanRaise"] = _raise is not null,
        ["HasTrackList"] = false,
        ["Identity"] = "Muses",
        ["DesktopEntry"] = "muses",
        ["SupportedUriSchemes"] = VariantValue.Array(System.Array.Empty<string>()),
        ["SupportedMimeTypes"] = VariantValue.Array(System.Array.Empty<string>())
    };

    private static Dictionary<string, VariantValue> PlayerProperties(Snapshot snapshot) => new()
    {
        ["PlaybackStatus"] = snapshot.Status,
        ["LoopStatus"] = LoopStatus(snapshot.Repeat),
        ["Rate"] = 1d,
        ["Shuffle"] = snapshot.Shuffle,
        ["Metadata"] = Metadata(snapshot),
        ["Volume"] = snapshot.Volume,
        ["Position"] = snapshot.Position,
        ["MinimumRate"] = 1d,
        ["MaximumRate"] = 1d,
        ["CanGoNext"] = snapshot.CanGoNext,
        ["CanGoPrevious"] = snapshot.CanGoPrevious,
        ["CanPlay"] = snapshot.CanPlay,
        ["CanPause"] = snapshot.CanPause,
        ["CanSeek"] = snapshot.CanSeek,
        ["CanControl"] = true
    };

    private static VariantValue Metadata(Snapshot snapshot)
    {
        var metadata = new Dict<string, VariantValue>();
        if (snapshot.Track is not { } track) return metadata;

        metadata["mpris:trackid"] = snapshot.TrackPath;
        metadata["xesam:title"] = track.Title;
        metadata["xesam:artist"] = VariantValue.Array(new[] { track.Artist });
        if (snapshot.Duration > 0) metadata["mpris:length"] = snapshot.Duration;
        if (!string.IsNullOrWhiteSpace(track.AlbumTitle)) metadata["xesam:album"] = track.AlbumTitle;
        if (Uri.TryCreate(track.ArtworkUrl, UriKind.Absolute, out var artwork) && artwork.Scheme is "http" or "https")
            metadata["mpris:artUrl"] = artwork.AbsoluteUri;
        return metadata;
    }

    private static string LoopStatus(RepeatMode mode) => mode switch
    {
        RepeatMode.One => "Track",
        RepeatMode.All => "Playlist",
        _ => "None"
    };

    private void PublishChanges()
    {
        if (!IsSupported || _connection is not { } connection) return;
        var previous = _published;
        var current = Capture();
        _published = current;
        var changes = new Dictionary<string, VariantValue>();
        if (previous?.Status != current.Status) changes["PlaybackStatus"] = current.Status;
        if (previous?.Repeat != current.Repeat) changes["LoopStatus"] = LoopStatus(current.Repeat);
        if (previous?.Shuffle != current.Shuffle) changes["Shuffle"] = current.Shuffle;
        if (previous?.Track != current.Track || previous?.TrackPath != current.TrackPath || previous?.Duration != current.Duration)
            changes["Metadata"] = Metadata(current);
        if (previous?.Volume != current.Volume) changes["Volume"] = current.Volume;
        if (previous?.CanGoNext != current.CanGoNext) changes["CanGoNext"] = current.CanGoNext;
        if (previous?.CanGoPrevious != current.CanGoPrevious) changes["CanGoPrevious"] = current.CanGoPrevious;
        if (previous?.CanPlay != current.CanPlay) changes["CanPlay"] = current.CanPlay;
        if (previous?.CanPause != current.CanPause) changes["CanPause"] = current.CanPause;
        if (previous?.CanSeek != current.CanSeek) changes["CanSeek"] = current.CanSeek;
        if (changes.Count == 0) return;

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(path: ObjectPath, @interface: PropertiesInterface, member: "PropertiesChanged", signature: "sa{sv}as");
        writer.WriteString(PlayerInterface);
        writer.WriteDictionary(changes);
        writer.WriteArray(System.Array.Empty<string>());
        connection.TrySendMessage(writer.CreateMessage());
    }

    private static void Reply(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(signature: null);
        context.Reply(writer.CreateMessage());
    }

    private async Task QuitAfterReplyAsync(DBusConnection connection, Action quit)
    {
        try
        {
            // The desktop shutdown disposes the connection. Flush the reply first.
            await connection.CallMethodAsync(CreateBusPing(connection)).WaitAsync(ConnectTimeout).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DBusExceptionBase or TimeoutException or ObjectDisposedException)
        {
            Trace.TraceWarning("MPRIS quit reply could not be confirmed: {0}", error.GetType().Name);
        }

        await DispatchUpdateAsync(() =>
        {
            if (!_disposed) quit();
        }).ConfigureAwait(false);
    }

    private static MessageBuffer CreateBusPing(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus.Peer", member: "Ping");
        return writer.CreateMessage();
    }
}
