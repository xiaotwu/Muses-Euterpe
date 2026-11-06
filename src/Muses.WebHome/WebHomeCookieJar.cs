using System.Diagnostics;
using Muses.Core.Advanced;

namespace Muses.WebHome;

public sealed class WebHomeCookieJar : IDisposable
{
    private const string NetscapeHeader = "# Netscape HTTP Cookie File\n";
    private const int MaximumCookieFileBytes = 10 * 1024 * 1024;
    private int _disposed;

    public string WorkspacePath { get; }
    public string CookieFilePath => Path.Combine(WorkspacePath, "cookies.txt");

    public WebHomeCookieJar(string? rootDirectory = null, Guid? id = null)
    {
        var root = rootDirectory ?? WebHomeWorkspace.RootPath;
        CreateWorkspaceDirectory(root);
        CleanupOrphans(root);
        WorkspacePath = Path.Combine(root, (id ?? Guid.NewGuid()).ToString("N"));
        CreateWorkspaceDirectory(WorkspacePath);
    }

    public void SeedNetscapeHeader()
    {
        using var writer = new StreamWriter(new FileStream(CookieFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        writer.Write(NetscapeHeader);
    }

    internal static async Task<IReadOnlyList<WebHomeCookie>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > MaximumCookieFileBytes)
            return [];

        using var reader = new StreamReader(stream);
        var cookies = new List<WebHomeCookie>();
        var charsRead = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            charsRead += line.Length + 1;
            if (charsRead > MaximumCookieFileBytes)
                return [];
            if (line.StartsWith("#HttpOnly_", StringComparison.Ordinal))
                line = line["#HttpOnly_".Length..];
            else if (line.StartsWith('#'))
                continue;

            var fields = line.Split('\t');
            if (fields.Length != 7 || !long.TryParse(fields[4], out var expiry) || expiry < 0)
                continue;
            var domain = fields[0].TrimStart('.').ToLowerInvariant();
            if (domain != "youtube.com" && !domain.EndsWith(".youtube.com", StringComparison.Ordinal))
                continue;
            var name = fields[5];
            var value = fields[6];
            if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
                || value.Any(c => c is < '!' or > '~' or ';') || !fields[2].StartsWith('/'))
                continue;

            cookies.Add(new WebHomeCookie(domain, fields[1] == "TRUE", fields[2], expiry, name, value));
        }

        return cookies;
    }

    public static async Task<bool> TryExportFromBrowserAsync(
        string ytdlpPath,
        string browserSpecification,
        string destinationCookieFile,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = ytdlpPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "--ignore-config", "--quiet", "--no-warnings",
            "--cookies-from-browser", browserSpecification, "--cookies", destinationCookieFile
        })
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info);
        if (process is null)
            return false;
        var discardOutput = Task.WhenAll(
            process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken),
            process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken));
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await discardOutput.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }

        // Export-only yt-dlp can emit a no-URL usage status after writing the cookie jar.
        return new FileInfo(destinationCookieFile).Length > NetscapeHeader.Length;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            DeleteWorkspace(WorkspacePath);
    }

    private static void CreateWorkspaceDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Web Home workspace cannot be a link.");
    }

    private static void CleanupOrphans(string root)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (Guid.TryParseExact(directory.Name, "N", out _) && directory.LastWriteTimeUtc < cutoff
                && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                DeleteWorkspace(directory.FullName);
        }
    }

    private static void DeleteWorkspace(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed record WebHomeCookie(string Domain, bool IncludeSubdomains, string Path, long Expiry, string Name, string Value)
{
    public bool AppliesTo(Uri uri, long now) =>
        (Expiry == 0 || Expiry > now)
        && (uri.Host == Domain || (IncludeSubdomains && uri.Host.EndsWith("." + Domain, StringComparison.Ordinal)))
        && (uri.AbsolutePath == Path || uri.AbsolutePath.StartsWith(Path.EndsWith('/') ? Path : Path + "/", StringComparison.Ordinal));
}
