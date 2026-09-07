using System.Text.Json;
using Muses.Core.Library;

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
    private readonly YTDlpRunner _runner;
    private readonly StreamUrlCache _cache;
    private string? _resolvedBinary;

    public YTDlpBridge(string? binaryPath = null, YTDlpRunner? runner = null, StreamUrlCache? cache = null)
    {
        _binaryPath = binaryPath;
        _runner = runner ?? new YTDlpRunner();
        _cache = cache ?? new StreamUrlCache();
    }

    public async Task<Uri> ResolveStreamUrlAsync(string videoId, string quality, TimeSpan timeout, CancellationToken ct = default)
    {
        if (_cache.TryGet(videoId, quality, out var cached)) return cached;
        var binary = await ResolveBinaryAsync().ConfigureAwait(false);
        var format = QualityMap.GetValueOrDefault(quality, QualityMap["bestaudio"]);
        var args = new List<string> { "-f", format, "-g", "--no-playlist", "--", videoId };
        var (stdout, _) = await _runner.RunAsync(binary, args, timeout, ct).ConfigureAwait(false);
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(line) || !Uri.TryCreate(line, UriKind.Absolute, out var url))
            throw new YTDlpException("yt-dlp output parse failed: no stream URL");
        _cache.Set(videoId, quality, url);
        return url;
    }

    public async Task<IReadOnlyList<YTDlpPlaylistEntry>> FetchPlaylistAsync(string url, TimeSpan timeout, CancellationToken ct = default)
    {
        var binary = await ResolveBinaryAsync().ConfigureAwait(false);
        var args = new[] { "--flat-playlist", "--dump-json", "--no-warnings", "--", url };
        var (stdout, _) = await _runner.RunAsync(binary, args, timeout, ct).ConfigureAwait(false);
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
        var binary = await ResolveBinaryAsync().ConfigureAwait(false);
        var args = new[] { "--flat-playlist", "--dump-json", "--no-warnings", $"ytsearch{limit}:{query}" };
        var (stdout, _) = await _runner.RunAsync(binary, args, timeout, ct).ConfigureAwait(false);
        return ParseNdjson(stdout);
    }

    public async Task<string?> VersionAsync()
    {
        try
        {
            var binary = await ResolveBinaryAsync().ConfigureAwait(false);
            var (stdout, _) = await _runner.RunAsync(binary, ["--version"], TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            return stdout.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<YTDlpPlaylistEntry?> FetchVideoMetadataAsync(string videoId, TimeSpan timeout, CancellationToken ct = default)
    {
        var binary = await ResolveBinaryAsync().ConfigureAwait(false);
        var args = new[] { "--dump-json", "--no-playlist", "--no-warnings", "--", videoId };
        var (stdout, _) = await _runner.RunAsync(binary, args, timeout, ct).ConfigureAwait(false);
        return ParseNdjson(stdout).FirstOrDefault() ?? ParseSingle(stdout);
    }

    private async Task<string> ResolveBinaryAsync()
    {
        if (_resolvedBinary is not null) return _resolvedBinary;
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(_binaryPath)) candidates.Add(_binaryPath);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "yt-dlp"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"));
        var repo = FindRepoYtDlp();
        if (repo is not null) candidates.Add(repo);
        foreach (var which in new[] { "yt-dlp", "yt-dlp.exe" })
        {
            var found = FindOnPath(which);
            if (found is not null) candidates.Add(found);
        }

        foreach (var path in candidates.Distinct())
        {
            if (File.Exists(path))
            {
                _resolvedBinary = path;
                return path;
            }
        }
        throw new YTDlpException("yt-dlp binary not found");
    }

    private static string? FindRepoYtDlp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var mac = Path.Combine(dir.FullName, "resources", "yt-dlp");
            var win = Path.Combine(dir.FullName, "resources", "yt-dlp.exe");
            if (File.Exists(mac)) return mac;
            if (File.Exists(win)) return win;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? FindOnPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists);
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
