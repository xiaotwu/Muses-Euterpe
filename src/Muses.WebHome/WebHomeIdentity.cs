using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Muses.WebHome;

/// <summary>
/// Resolves a YouTube channel id from an ephemeral cookie jar inside the helper.
/// Uses yt-dlp against the signed-in Liked Videos playlist (LL) — the user's own
/// cookies after opt-in, never a scrape in the Avalonia process.
/// </summary>
public static class WebHomeIdentity
{
    private static readonly Regex ChannelId = new(@"UC[\w-]{10,}", RegexOptions.Compiled);

    public static async Task<string?> TryResolveChannelAsync(
        string ytdlpPath,
        string cookieFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ytdlpPath) || !File.Exists(ytdlpPath)) return null;
        if (string.IsNullOrWhiteSpace(cookieFile) || !File.Exists(cookieFile)) return null;

        var psi = new ProcessStartInfo
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
                     "--cookies", cookieFile,
                     "--flat-playlist", "--playlist-end", "1",
                     "--print", "%(channel_id,uploader_id,id)s",
                     "--", "https://www.youtube.com/playlist?list=LL"
                 })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc is null) return null;
        try
        {
            var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var m = ChannelId.Match(stdout ?? "");
            return m.Success ? m.Value : null;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }
        catch
        {
            return null;
        }
    }
}
