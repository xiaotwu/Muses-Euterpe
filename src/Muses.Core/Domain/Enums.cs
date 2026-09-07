namespace Muses.Core.Domain;

public enum TrackMediaKind
{
    Song,
    MusicVideo
}

public enum RepeatMode
{
    Off,
    All,
    One
}

public static class RepeatModeCodec
{
    public static RepeatMode Parse(string? raw) => raw switch
    {
        "all" => RepeatMode.All,
        "one" => RepeatMode.One,
        _ => RepeatMode.Off
    };

    public static string Wire(RepeatMode mode) => mode switch
    {
        RepeatMode.All => "all",
        RepeatMode.One => "one",
        _ => "off"
    };

    public static RepeatMode Next(RepeatMode mode) => mode switch
    {
        RepeatMode.Off => RepeatMode.One,
        RepeatMode.One => RepeatMode.All,
        _ => RepeatMode.Off
    };
}

public enum QueueSource
{
    Album,
    Playlist,
    Import,
    Search,
    Songs,
    Artist,
    Recently
}

public static class QueueSourceCodec
{
    public static string Wire(QueueSource source) => source switch
    {
        QueueSource.Album => "album",
        QueueSource.Playlist => "playlist",
        QueueSource.Import => "import",
        QueueSource.Search => "search",
        QueueSource.Artist => "artist",
        QueueSource.Recently => "recently",
        _ => "songs"
    };

    public static QueueSource Parse(string? raw) => raw switch
    {
        "album" => QueueSource.Album,
        "playlist" => QueueSource.Playlist,
        "import" => QueueSource.Import,
        "search" => QueueSource.Search,
        "artist" => QueueSource.Artist,
        "recently" => QueueSource.Recently,
        _ => QueueSource.Songs
    };
}

public enum QueueHistoryState
{
    Played,
    Skipped,
    Removed
}

public enum MetadataStatus
{
    Embedded,
    Enriching,
    Complete,
    Missing
}

public enum TrackAvailability
{
    Available,
    Unavailable
}

public enum YouTubeLinkKind
{
    Video,
    Playlist,
    Unknown
}
