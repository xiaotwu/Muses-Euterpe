using System.Runtime.InteropServices;
using Muses.Core.Advanced;

namespace Muses.WebHome;

/// <summary>
/// Ephemeral, permission-restricted cookie workspace owned by the helper process.
/// Always deleted on Dispose / process exit — never stored under the Avalonia app data root.
/// </summary>
public sealed class WebHomeCookieJar : IDisposable
{
    public const string RootFolderName = "muses-web-home-helper";

    private readonly string _workspace;
    private int _disposed;

    public string WorkspacePath => _workspace;
    public string CookieFilePath => Path.Combine(_workspace, "cookies.txt");

    public WebHomeCookieJar(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(Path.GetTempPath(), RootFolderName);
        Directory.CreateDirectory(root);
        TryRestrictDirectory(root);
        _workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        TryRestrictDirectory(_workspace);
    }

    public void SeedNetscapeHeader()
    {
        File.WriteAllText(CookieFilePath, "# Netscape HTTP Cookie File\n");
        TryRestrictFile(CookieFilePath);
    }

    public bool HasCookiesBeyondHeader()
    {
        if (!File.Exists(CookieFilePath)) return false;
        var text = File.ReadAllText(CookieFilePath);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Any(l => !l.StartsWith('#') && l.Contains('\t'));
    }

    /// <summary>
    /// Optional channel hint written by tests / future identity probe into the jar workspace.
    /// Production probe uses this only when present; otherwise returns Unavailable.
    /// </summary>
    public void WriteChannelHint(string channelId) =>
        File.WriteAllText(Path.Combine(_workspace, "channel.txt"), channelId.Trim());

    public string? ReadChannelHint()
    {
        var path = Path.Combine(_workspace, "channel.txt");
        if (!File.Exists(path)) return null;
        var value = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static async Task<bool> TryExportFromBrowserAsync(
        string ytdlpPath,
        string browserSpecification,
        string destinationCookieFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ytdlpPath) || !File.Exists(ytdlpPath))
            return false;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ytdlpPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in new[]
                 {
                     "--ignore-config", "--quiet", "--no-warnings",
                     "--cookies-from-browser", browserSpecification,
                     "--cookies", destinationCookieFile
                 })
            psi.ArgumentList.Add(a);

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null) return false;
        try
        {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        return File.Exists(destinationCookieFile) && new FileInfo(destinationCookieFile).Length > 40;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (Directory.Exists(_workspace))
                Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // Best-effort wipe; helper process exit also drops the temp tree.
        }
    }

    private static void TryRestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // 0700 owner-only
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch { /* best effort */ }
        }
    }

    private static void TryRestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* best effort */ }
        }
    }
}
