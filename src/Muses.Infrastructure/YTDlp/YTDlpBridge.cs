using System.Text.Json;
using Muses.Core.Library;
using Muses.Core.Platform;

namespace Muses.Infrastructure.YTDlp;

public sealed class YTDlpBridge : IYTDlpBridge, IYouTubePlaylistFetcher
{
    private static readonly Dictionary<string, string> QualityMap = new()
    {
        ["bestaudio"] = "bestaudio[ext*=m4a]/bestaudio/best",
        ["256k"] = "ba[abr<=256]/bestaudio[abr<=256]",
        ["128k"] = "ba[abr<=128]",
        ["64k"] = "ba[abr<=64]",
        ["best"] = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best",
        ["1080p"] = "best[height<=1080][ext=mp4]/best[height<=1080]",
        ["720p"] = "best[height<=720][ext=mp4]/best[height<=720]"
    };

    private readonly string? _binaryPath;
    private readonly string? _javaScriptRuntimePath;
    private readonly YTDlpRunner _runner;
    private readonly StreamUrlCache _cache;
    private readonly object _audioContext = new();
    private string? _resolvedBinary;

    public YTDlpBridge(string? binaryPath = null, YTDlpRunner? runner = null, StreamUrlCache? cache = null,
        string? javaScriptRuntimePath = null)
    {
        _binaryPath = binaryPath ?? Environment.GetEnvironmentVariable("MUSES_YTDLP_PATH");
        _javaScriptRuntimePath = javaScriptRuntimePath ?? Environment.GetEnvironmentVariable("MUSES_DENO_PATH");
        _runner = runner ?? new YTDlpRunner();
        _cache = cache ?? new StreamUrlCache();
    }

    public async Task<ResolvedAudioStream> ResolveAudioStreamAsync(
        string videoId, string quality, TimeSpan timeout, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (videoId.Length != 11 || !videoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new YTDlpException("Invalid video identity");
        if (_cache.GetAudio(_audioContext, videoId, quality) is { } cached)
            return cached;

        var binary = ResolveBinary();
        var args = CommandOptions(binary, ignoreConfig: true);
        args.AddRange(["--no-cookies", "--no-cookies-from-browser", "--no-plugin-dirs", "--no-remote-components",
            "--socket-timeout", "5", "--extractor-retries", "0", "--retries", "0",
            "--skip-download", "--no-playlist", "--dump-single-json", "--check-formats",
            "--format-sort-force", "-S", "abr,asr", "-f", AudioFormat(quality), "--", $"https://youtu.be/{videoId}"]);
        var stdout = await _runner.RunAudioAsync(binary, args, timeout, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var stream = AudioStreamJsonParser.Parse(stdout, videoId, DateTimeOffset.UtcNow);
        _cache.SetAudio(_audioContext, videoId, quality, stream);
        return stream;
    }

    public void InvalidateAudioStream(string videoId, string quality, ResolvedAudioStream stream) =>
        _cache.InvalidateAudio(_audioContext, videoId, quality, stream);

    private static string AudioFormat(string quality) => quality switch
    {
        "256k" or "128k" or "64k" => QualityMap[quality],
        _ => "bestaudio"
    };

    public async Task<Uri> ResolveStreamUrlAsync(string videoId, string quality, TimeSpan timeout, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_cache.TryGet(videoId, quality, out var cached)) return cached;
        var format = QualityMap.GetValueOrDefault(quality, QualityMap["bestaudio"]);
        var (stdout, _) = await RunAsync(["-f", format, "-g", "--no-playlist", "--", videoId], timeout, ct).ConfigureAwait(false);
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(line) || !Uri.TryCreate(line, UriKind.Absolute, out var url))
            throw new YTDlpException("yt-dlp output parse failed: no stream URL");
        _cache.Set(videoId, quality, url);
        return url;
    }

    public async Task<IReadOnlyList<YTDlpPlaylistEntry>> FetchPlaylistAsync(string url, TimeSpan timeout, CancellationToken ct = default)
    {
        var (stdout, _) = await RunAsync(["--flat-playlist", "--dump-json", "--no-warnings", "--", url], timeout, ct)
            .ConfigureAwait(false);
        return ParseNdjson(stdout);
    }

    async Task<IReadOnlyList<YouTubePlaylistEntry>> IYouTubePlaylistFetcher.FetchPlaylistAsync(string url, CancellationToken ct)
    {
        var entries = await FetchPlaylistAsync(url, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
        return entries.Select(e => new YouTubePlaylistEntry(
            e.Id,
            e.Title,
            e.Uploader,
            e.Duration,
            e.PlaylistTitle,
            e.ChannelId,
            e.Track,
            e.Album,
            e.ReleaseYear)).ToList();
    }

    public async Task<IReadOnlyList<YTDlpPlaylistEntry>> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct = default)
    {
        var (stdout, _) = await RunAsync(["--flat-playlist", "--dump-json", "--no-warnings", $"ytsearch{limit}:{query}"],
            timeout, ct).ConfigureAwait(false);
        return ParseNdjson(stdout);
    }

    public async Task<string?> VersionAsync()
    {
        try
        {
            var (stdout, _) = await RunAsync(["--version"], TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            return stdout.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<YTDlpPlaylistEntry?> FetchVideoMetadataAsync(string videoId, TimeSpan timeout, CancellationToken ct = default)
    {
        var (stdout, _) = await RunAsync(["--dump-json", "--no-playlist", "--no-warnings", "--", videoId], timeout, ct)
            .ConfigureAwait(false);
        return ParseNdjson(stdout).FirstOrDefault() ?? ParseSingle(stdout);
    }

    private Task<(string Stdout, string Stderr)> RunAsync(string[] command, TimeSpan timeout, CancellationToken ct = default)
    {
        var binary = ResolveBinary();
        var ignoreConfig = binary == MediaToolPaths.FindPackaged("yt-dlp") ||
                           Path.IsPathFullyQualified(Environment.GetEnvironmentVariable("MUSES_CACHE_HOME") ?? "") ||
                           Path.IsPathFullyQualified(Environment.GetEnvironmentVariable("MUSES_DATA_HOME") ?? "");
        var args = CommandOptions(binary, ignoreConfig);
        args.AddRange(command);
        return _runner.RunAsync(binary, args, timeout, ct);
    }

    private List<string> CommandOptions(string binary, bool ignoreConfig)
    {
        var args = new List<string> { "--cache-dir", Path.Combine(LocalDataMaintenance.LocalCacheRoot(), "yt-dlp") };
        if (ignoreConfig)
            args.Add("--ignore-config");

        var deno = !string.IsNullOrWhiteSpace(_javaScriptRuntimePath)
            ? Path.GetFullPath(_javaScriptRuntimePath)
            : MediaToolPaths.FindPackaged("deno") ?? FindExecutable("deno.exe", Path.GetDirectoryName(binary));
        if (deno is not null)
        {
            if (!File.Exists(deno))
                throw new YTDlpException("Configured Deno binary not found");
            args.AddRange(["--js-runtimes", $"deno:{deno}"]);
        }
        return args;
    }

    private string ResolveBinary()
    {
        if (_resolvedBinary is not null) return _resolvedBinary;
        if (!string.IsNullOrWhiteSpace(_binaryPath))
        {
            if (!File.Exists(_binaryPath))
                throw new YTDlpException("Configured yt-dlp binary not found");
            return _resolvedBinary = Path.GetFullPath(_binaryPath);
        }
        return _resolvedBinary = MediaToolPaths.FindPackaged("yt-dlp") ?? FindExecutable("yt-dlp.exe")
            ?? throw new YTDlpException("yt-dlp binary not found");
    }

    private static string? FindExecutable(string name, string? preferredDirectory = null)
    {
        if (preferredDirectory is not null && File.Exists(Path.Combine(preferredDirectory, name)))
            return Path.Combine(preferredDirectory, name);

        var bundled = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(bundled)) return bundled;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "resources", name);
            if (File.Exists(candidate)) return candidate;
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var path = paths.Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists);
        return path is null ? null : Path.GetFullPath(path);
    }

    private static IReadOnlyList<YTDlpPlaylistEntry> ParseNdjson(string stdout)
    {
        var list = new List<YTDlpPlaylistEntry>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            var entry = ParseSingle(line);
            if (entry is not null) list.Add(entry);
        }
        return list;
    }

    private static YTDlpPlaylistEntry? ParseSingle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || !root.TryGetProperty("title", out var titleEl))
                return null;
            return new YTDlpPlaylistEntry(
                Id: idEl.GetString() ?? "",
                Title: titleEl.GetString() ?? "",
                Uploader: GetString(root, "uploader"),
                Duration: GetDouble(root, "duration"),
                PlaylistTitle: GetString(root, "playlist_title") ?? GetString(root, "playlist"),
                ChannelId: GetString(root, "channel_id") ?? GetString(root, "uploader_id"),
                Track: GetString(root, "track"),
                Album: GetString(root, "album"),
                ReleaseYear: GetInt(root, "release_year"));
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(el.GetString()) ? null : el.GetString())
            : null;

    private static double? GetDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
    }

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;
    }
}
