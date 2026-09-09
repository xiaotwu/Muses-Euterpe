using Muses.Core.Advanced;
using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Preferences;
using Muses.Core.Queue;
using Muses.Infrastructure.Advanced;
using Muses.Infrastructure.Playback;
using Muses.Persistence;
using Muses.WebHome;
using Xunit;

namespace Muses.Tests;

public sealed class Phase4EqAndWebHomeTests
{
    [Fact]
    public void EQService_band_changes_push_SetEq_on_engine_double()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var engine = new FakePlayerEngine();
        var playback = new PlaybackService(engine, new QueueService());
        var eq = new EQService(store);
        eq.BandsChanged += bands => playback.SetEq(EQService.ToEngineBands(bands));

        eq.SelectPreset("BassBoost");
        Assert.True(engine.SetEqCallCount >= 1);
        Assert.NotNull(engine.LastEqBands);
        Assert.Equal(32, engine.LastEqBands!.Count);
        Assert.True(engine.LastEqBands[0].Gain > 0);

        var before = engine.SetEqCallCount;
        eq.SetBandGain(0, 12f);
        Assert.Equal(before + 1, engine.SetEqCallCount);
        Assert.Equal(12f, engine.LastEqBands[0].Gain);
        Assert.True(engine.SupportsEq);
        Assert.True(playback.EngineSupportsEq);
    }

    [Fact]
    public void ProcessStreamEngine_BuildEqAfChain_emits_mpv_equalizer_filters()
    {
        var bands = new List<EqBand>
        {
            new(100, 6f, 1f),
            new(1000, 0f, 1f),
            new(8000, -3f, 1.5f)
        };
        var chain = ProcessStreamEngine.BuildEqAfChain(bands);
        Assert.Contains("equalizer=f=100", chain);
        Assert.Contains("g=6", chain);
        Assert.Contains("equalizer=f=8000", chain);
        Assert.DoesNotContain("f=1000", chain); // near-flat skipped
    }

    [Fact]
    public async Task ProcessStreamEngine_SetEq_forwards_af_to_session()
    {
        var factory = new EqAwareFakeMpvFactory();
        await using var owner = new EngineOwner(new ProcessStreamEngine(new NoopBridge(), factory));
        await owner.Inner.LoadAsync(TrackSnapshot.Test("eq", "vid-eq", 30));

        owner.Inner.SetEq([new EqBand(250, 4.5f, 1f)]);
        Assert.NotNull(factory.LastSession);
        Assert.Contains("equalizer=f=250", factory.LastSession!.LastAf);
        Assert.Contains("g=4.5", factory.LastSession.LastAf);
    }

    [Fact]
    public async Task WebHome_probe_Available_Unavailable_AccountMismatch_Closed()
    {
        var prefs = new MemoryPreferences();
        // Closed: no channel
        var closedClient = new FakeWebHomeHelperClient(WebHomeResponse.Available("UC_x"));
        var closed = new WebHomeSessionController(closedClient, () => null, prefs);
        await closed.ProbeSessionAsync();
        Assert.Equal(WebHomeSessionStatus.Closed, closed.Status);
        Assert.Equal(0, closedClient.ExecuteCount);

        // Default off → Closed even with channel (never launches helper)
        prefs.SetBool(PrefKey.WebHomeEnabled, false);
        var offClient = new FakeWebHomeHelperClient(WebHomeResponse.Available("UC_one"));
        var off = new WebHomeSessionController(offClient, () => "UC_one", prefs);
        await off.ProbeSessionAsync();
        Assert.Equal(WebHomeSessionStatus.Closed, off.Status);
        Assert.Equal(0, offClient.ExecuteCount);

        // Consent + enable → Available
        var okClient = new FakeWebHomeHelperClient(WebHomeResponse.Available("UC_one"));
        var ok = new WebHomeSessionController(okClient, () => "UC_one", prefs);
        ok.EnableWithConsent();
        await ok.ProbeSessionAsync();
        Assert.Equal(WebHomeSessionStatus.Available, ok.Status);
        Assert.Equal(1, okClient.ExecuteCount);
        Assert.Equal("probeSession", okClient.LastRequest?.Action);

        // AccountMismatch
        var badClient = new FakeWebHomeHelperClient(WebHomeResponse.Mismatch("UC_other"));
        var bad = new WebHomeSessionController(badClient, () => "UC_one", prefs);
        bad.EnableWithConsent();
        await bad.ProbeSessionAsync();
        Assert.Equal(WebHomeSessionStatus.AccountMismatch, bad.Status);

        // Unavailable
        var unClient = new FakeWebHomeHelperClient(WebHomeResponse.Unavailable("cookieSourceUnavailable"));
        var un = new WebHomeSessionController(unClient, () => "UC_one", prefs);
        un.EnableWithConsent();
        await un.ProbeSessionAsync();
        Assert.Equal(WebHomeSessionStatus.Unavailable, un.Status);
    }

    [Fact]
    public void WebHomeCookieJar_deletes_workspace_on_dispose()
    {
        string workspace;
        using (var jar = new WebHomeCookieJar())
        {
            jar.SeedNetscapeHeader();
            workspace = jar.WorkspacePath;
            Assert.True(Directory.Exists(workspace));
            Assert.True(File.Exists(jar.CookieFilePath));
        }
        Assert.False(Directory.Exists(workspace));
    }

    [Fact]
    public async Task WebHomeProbeCommand_file_source_with_channel_hint_can_match_or_mismatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muses-wh-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cookiePath = Path.Combine(dir, "cookies.txt");
            await File.WriteAllTextAsync(cookiePath, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tSID\ttest\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "channel.txt"), "UC_expected");

            var available = await WebHomeProbeCommand.ExecuteAsync(new WebHomeRequest(
                "probeSession", "UC_expected",
                new WebHomeCookieSourceDescriptor("file", FilePath: cookiePath),
                "en", "US"));
            Assert.True(available.IsAvailable);
            Assert.Equal("UC_expected", available.ChannelId);

            var mismatch = await WebHomeProbeCommand.ExecuteAsync(new WebHomeRequest(
                "probeSession", "UC_other",
                new WebHomeCookieSourceDescriptor("file", FilePath: cookiePath),
                "en", "US"));
            Assert.False(mismatch.IsAvailable);
            Assert.Equal("accountMismatch", mismatch.ErrorCode);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PrefKey_WebHomeEnabled_defaults_off()
    {
        var prefs = new MemoryPreferences();
        Assert.False(prefs.GetBool(PrefKey.WebHomeEnabled, false));
        Assert.Equal("0", prefs.GetString(PrefKey.WebHomeConsentVersion, "0"));
    }

    [Fact]
    public void AudioNerd_does_not_fabricate_device_names()
    {
        var rows = AudioInfoModel.BuildRows(null, null, null, 0.5);
        Assert.Contains(rows, r => (r.Label == "Output Device" || r.Label == "输出设备") && r.Value == AudioInfoModel.Unknown);
        // CaptureCurrent without a device stays null — never invents "Realtek" etc.
        var ctx = ListeningContext.CaptureCurrent();
        Assert.Null(ctx.OutputDeviceName);
        Assert.False(ctx.IsHeadphones);
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

    private sealed class EqAwareFakeMpvFactory : IMpvPlayerFactory
    {
        public EqAwareFakeSession? LastSession { get; private set; }
        public string? FindBinary() => "/fake/mpv";
        public IMpvPlayerSession Start(Uri url, int initialVolume0to100)
        {
            LastSession = new EqAwareFakeSession(initialVolume0to100);
            return LastSession;
        }
    }

    private sealed class EqAwareFakeSession : IMpvPlayerSession
    {
        private int _disposed;
        public EqAwareFakeSession(int volume) => LastVolume = volume;
        public bool IsAlive => _disposed == 0;
        public double TimePos { get; private set; }
        public string LastAf { get; private set; } = "";
        public int LastVolume { get; private set; }
        public event Action? EndOfFile
        {
            add { }
            remove { }
        }
        public event Action? Exited;
        public void SetPause(bool paused) { }
        public void SeekAbsolute(double seconds) => TimePos = seconds;
        public void SetVolume(int volume0to100) => LastVolume = volume0to100;
        public void SetAudioFilters(string? afChain) => LastAf = afChain ?? "";
        public Task<double?> GetTimePosAsync(CancellationToken cancellationToken = default) => Task.FromResult<double?>(TimePos);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Exited?.Invoke();
        }
    }
}
