using Muses.Core.Advanced;
using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Platform;
using Muses.Core.Preferences;
using Muses.Core.Queue;
using Muses.Infrastructure;
using Muses.Infrastructure.Playback;
using Muses.WebHome;
using Xunit;

namespace Muses.Tests;

public sealed class Stage3Tests
{
    [Fact]
    public async Task WebHome_identity_resolver_matches_expected_channel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muses-wh-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cookiePath = Path.Combine(dir, "cookies.txt");
            await File.WriteAllTextAsync(cookiePath, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tSID\ttest\n");

            var available = await WebHomeProbeCommand.ExecuteAsync(
                new WebHomeRequest("probeSession", "UC_from_cookies",
                    new WebHomeCookieSourceDescriptor("file", FilePath: cookiePath), "en", "US"),
                resolveChannelFromCookies: (_, _) => Task.FromResult<string?>("UC_from_cookies"));
            Assert.True(available.IsAvailable);
            Assert.Equal("UC_from_cookies", available.ChannelId);

            var mismatch = await WebHomeProbeCommand.ExecuteAsync(
                new WebHomeRequest("probeSession", "UC_other",
                    new WebHomeCookieSourceDescriptor("file", FilePath: cookiePath), "en", "US"),
                resolveChannelFromCookies: (_, _) => Task.FromResult<string?>("UC_from_cookies"));
            Assert.Equal("accountMismatch", mismatch.ErrorCode);

            var none = await WebHomeProbeCommand.ExecuteAsync(
                new WebHomeRequest("probeSession", "UC_from_cookies",
                    new WebHomeCookieSourceDescriptor("file", FilePath: cookiePath), "en", "US"),
                resolveChannelFromCookies: (_, _) => Task.FromResult<string?>(null));
            Assert.Equal("identityUnavailable", none.ErrorCode);
            Assert.False(none.IsAvailable);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void NullGlobalHotkeys_is_off_and_unsupported()
    {
        var h = NullGlobalHotkeys.Instance;
        Assert.False(h.IsSupported);
        Assert.False(h.IsEnabled);
        h.SetEnabled(true);
        Assert.False(h.IsEnabled);
    }

    [Fact]
    public void AppThemeCodec_round_trips_light()
    {
        Assert.Equal(AppTheme.Light, AppThemeCodec.Parse("light"));
        Assert.Equal("light", AppThemeCodec.Wire(AppTheme.Light));
        Assert.Equal(AppTheme.Dark, AppThemeCodec.Parse(null));
    }

    [Fact]
    public void LocalDataMaintenance_clear_cache_is_best_effort()
    {
        var n = LocalDataMaintenance.ClearCache();
        Assert.True(n >= 0);
    }

    [Fact]
    public void AudioInfo_unknown_device_when_probe_is_null()
    {
        var rows = AudioInfoModel.BuildRows(null, NullAudioOutputProbe.Instance.DefaultDeviceName, null, 0.4,
            NullAudioOutputProbe.Instance.LatencyMilliseconds);
        Assert.Contains(rows, r => (r.Label == "Output Device" || r.Label == "输出设备") && r.Value == AudioInfoModel.Unknown);
        Assert.Contains(rows, r => (r.Label == "Device period" || r.Label == "设备周期") && r.Value == AudioInfoModel.Unknown);
        var ctx = ListeningContext.CaptureCurrent();
        Assert.Null(ctx.OutputDeviceName);
    }

    [Fact]
    public void AudioInfo_uses_provided_device_name_without_inventing()
    {
        var rows = AudioInfoModel.BuildRows(null, "Speakers (USB DAC)", null, 1, 10);
        Assert.Contains(rows, r => r.Value == "Speakers (USB DAC)");
        Assert.Contains(rows, r => r.Value == "10.0 ms" || r.Value == "10 ms");
    }

    [Fact]
    public void PlaybackService_SetCrossfadeSeconds_reaches_engine()
    {
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        playback.SetCrossfadeSeconds(2.5);
        Assert.Equal(2.5, engine.CrossfadeSeconds);
        playback.SetCrossfadeSeconds(99);
        Assert.Equal(12, engine.CrossfadeSeconds);
    }

    [Fact]
    public async Task ProcessStreamEngine_crossfade_starts_second_session_before_disposing_first()
    {
        var factory = new LiveCountFactory();
        await using var owner = new EngineOwner(new ProcessStreamEngine(new NoopBridge(), factory));
        owner.Inner.SetCrossfadeSeconds(0.4);
        await owner.Inner.LoadAsync(TrackSnapshot.Test("a", "vid-a", 30));
        Assert.Equal(1, factory.Live);
        await owner.Inner.LoadAsync(TrackSnapshot.Test("b", "vid-b", 30));
        Assert.True(factory.Started >= 2);
        Assert.True(factory.PeakLive >= 2);
    }

    private sealed class LiveCountFactory : IMpvPlayerFactory
    {
        public int Started { get; private set; }
        public int Live { get; private set; }
        public int PeakLive { get; private set; }
        public string? FindBinary() => "/fake/mpv";
        public IMpvPlayerSession Start(Uri url, int initialVolume0to100)
        {
            Started++;
            Live++;
            if (Live > PeakLive) PeakLive = Live;
            return new CountingSession(this, initialVolume0to100);
        }

        private void OnGone() => Live = Math.Max(0, Live - 1);

        private sealed class CountingSession : IMpvPlayerSession
        {
            private readonly LiveCountFactory _owner;
            private int _disposed;
            public CountingSession(LiveCountFactory owner, int volume)
            {
                _owner = owner;
                LastVolume = volume;
            }
            public bool IsAlive => _disposed == 0;
            public double TimePos { get; private set; }
            public int LastVolume { get; private set; }
            public event Action? EndOfFile { add { } remove { } }
            public event Action? Exited;
            public void SetPause(bool paused) { }
            public void SeekAbsolute(double seconds) => TimePos = seconds;
            public void SetVolume(int volume0to100) => LastVolume = volume0to100;
            public void SetAudioFilters(string? afChain) { }
            public Task<double?> GetTimePosAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<double?>(TimePos);
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _owner.OnGone();
                try { Exited?.Invoke(); } catch { /* ignore */ }
            }
        }
    }

    private sealed class EngineOwner : IAsyncDisposable
    {
        public ProcessStreamEngine Inner { get; }
        public EngineOwner(ProcessStreamEngine engine) => Inner = engine;
        public ValueTask DisposeAsync()
        {
            Inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopBridge : Muses.Infrastructure.YTDlp.IYTDlpBridge
    {
        public Task<Uri> ResolveStreamUrlAsync(string videoId, string quality, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult(new Uri($"https://example.com/{videoId}.m4a"));
        public Task<IReadOnlyList<Muses.Infrastructure.YTDlp.YTDlpPlaylistEntry>> FetchPlaylistAsync(string url, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Muses.Infrastructure.YTDlp.YTDlpPlaylistEntry>>([]);
        public Task<IReadOnlyList<Muses.Infrastructure.YTDlp.YTDlpPlaylistEntry>> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Muses.Infrastructure.YTDlp.YTDlpPlaylistEntry>>([]);
        public Task<Muses.Infrastructure.YTDlp.YTDlpPlaylistEntry?> FetchVideoMetadataAsync(string videoId, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<Muses.Infrastructure.YTDlp.YTDlpPlaylistEntry?>(null);
        public Task<string?> VersionAsync() => Task.FromResult<string?>("fake");
    }
}
