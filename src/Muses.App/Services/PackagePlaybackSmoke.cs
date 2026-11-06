using System.Diagnostics;
using System.Text.Json;
using Muses.Core.Domain;
using Muses.Core.Library;
using Muses.Core.Platform;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Infrastructure.Playback;

namespace Muses.App.Services;

// The package harness can only run against an explicit isolated data directory.
internal sealed class PackagePlaybackSmoke(string mode, string videoId, string dataDirectory)
{
    internal const string Argument = "--verify-package-playback";

    internal static PackagePlaybackSmoke? Parse(string[]? args)
    {
        var dataDirectory = Environment.GetEnvironmentVariable("MUSES_DATA_HOME");
        return args is [Argument, "start" or "resume", var videoId]
            && videoId.Length == 11 && videoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            && Path.IsPathFullyQualified(dataDirectory ?? "")
            ? new PackagePlaybackSmoke(args[1], videoId, dataDirectory!)
            : null;
    }

    internal async Task<int> RunAsync(PlaybackService playback, LibraryService library)
    {
        var mpv = MpvPlayerFactory.LocateMpvBinary();
        var stage = "media-tools";
        string? failure = null;
        double? restoredPosition = null;
        try
        {
            if (MediaToolPaths.FindPackaged("yt-dlp") is null || MediaToolPaths.FindPackaged("deno") is null ||
                mpv != MediaToolPaths.FindPackaged("mpv") ||
                mpv is null)
                throw new InvalidOperationException("The package media tools could not be selected.");

            var state = playback.State;
            if (mode == "start")
            {
                stage = "first-playback";
                var track = library.UpsertFromYouTube(videoId, "Package playback fixture", "", 0, null, saveToLibrary: true);
                playback.PlayTrack(track, [track], QueueSource.Search);
                await WaitAsync(state, () => state.IsPlaying && state.Position >= 2, TimeSpan.FromSeconds(52));
                stage = "seek";
                playback.Seek(12);
                await WaitAsync(state, () => state.IsPlaying && state.Position >= 12.4, TimeSpan.FromSeconds(6));
            }
            else
            {
                stage = "paused-restore";
                var saved = playback.Queue.LastPositionMs.GetValueOrDefault() / 1000;
                if (playback.Queue.Current()?.Track.YouTubeId != videoId || saved < 12)
                    throw new InvalidOperationException("No matching saved position.");
                await playback.RestoreCurrentPausedAsync().WaitAsync(TimeSpan.FromSeconds(52));
                if (state.Track?.YouTubeId != videoId || state.Error is not null || state.IsPlaying ||
                    Math.Abs(state.Position - saved) > 1)
                    throw new InvalidOperationException("The saved track did not restore paused.");
                restoredPosition = state.Position;
                stage = "resumed-playback";
                playback.Play();
                await WaitAsync(state, () => state.IsPlaying && state.Position > saved + 0.4, TimeSpan.FromSeconds(6));
            }

            stage = "save";
            playback.Pause();
            if (playback.Queue.LastPositionMs.GetValueOrDefault() < 12000)
                throw new InvalidOperationException("The listening position was not saved.");
            stage = "complete";
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException)
        {
            failure = playback.State.Error?.Kind.ToString() ?? error.Message;
        }

        var report = JsonSerializer.Serialize(new
        {
            mode, stage, videoId, failure, mpv, restoredPosition,
            position = playback.State.Position,
            savedPositionMs = playback.Queue.LastPositionMs
        });
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, $"playback-smoke-{mode}.json"), report);
        return failure is null ? 0 : 1;
    }

    private static async Task WaitAsync(PlayerState state, Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (state.Error is null && !condition() && elapsed.Elapsed < timeout)
            await Task.Delay(100);
        if (state.Error is not null || !condition())
            throw new TimeoutException("The player did not advance within the allotted time.");
    }
}
