using Muses.Core.Domain;
using Muses.Infrastructure.Playback;
using Muses.Infrastructure.YTDlp;

namespace Muses.Tests;

public class ProcessStreamEngineTests
{
    [Fact]
    public async Task Pause_does_not_raise_completion_and_keeps_session()
    {
        var bridge = new FakeBridge();
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(bridge, factory));

        var track = TrackSnapshot.Test("song", "vid-1", durationSeconds: 120);
        await engine.Inner.LoadAsync(track);
        Assert.True(engine.Inner.State.IsPlaying);
        Assert.Equal(1, bridge.ResolveCount);
        Assert.NotNull(factory.LastSession);

        var completed = 0;
        engine.Inner.OnCompletion = () => completed++;

        engine.Inner.Pause();
        Assert.False(engine.Inner.State.IsPlaying);
        Assert.True(factory.LastSession!.IsAlive);
        Assert.True(factory.LastSession.Paused);
        Assert.Equal(0, completed);

        // Spurious exit while suppressed/paused path should not complete either.
        factory.LastSession.RaiseExited();
        await Task.Delay(20);
        Assert.Equal(0, completed);
    }

    [Fact]
    public async Task Resume_unpauses_without_new_yt_dlp_resolve()
    {
        var bridge = new FakeBridge();
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(bridge, factory));

        await engine.Inner.LoadAsync(TrackSnapshot.Test("song", "vid-1", 90));
        Assert.Equal(1, bridge.ResolveCount);
        var session = factory.LastSession!;

        engine.Inner.Pause();
        Assert.True(session.Paused);

        engine.Inner.Play();
        Assert.True(engine.Inner.State.IsPlaying);
        Assert.False(session.Paused);
        Assert.Equal(1, bridge.ResolveCount);
        Assert.Same(session, factory.LastSession);
    }

    [Fact]
    public async Task Seek_is_forwarded_to_session()
    {
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(new FakeBridge(), factory));
        await engine.Inner.LoadAsync(TrackSnapshot.Test("song", "vid-1", 200));

        engine.Inner.Seek(42.5);
        Assert.Equal(42.5, engine.Inner.State.Position);
        Assert.Equal(42.5, factory.LastSession!.LastSeek);
        Assert.Equal(42.5, factory.LastSession.TimePos);
    }

    [Fact]
    public async Task SetVolume_maps_0_1_to_0_100()
    {
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(new FakeBridge(), factory));
        await engine.Inner.LoadAsync(TrackSnapshot.Test("song", "vid-1", 30));

        engine.Inner.SetVolume(0.55f);
        Assert.Equal(55, factory.LastSession!.LastVolume);

        engine.Inner.SetVolume(1.5f);
        Assert.Equal(100, factory.LastSession.LastVolume);

        engine.Inner.SetVolume(-1f);
        Assert.Equal(0, factory.LastSession.LastVolume);
    }

    [Fact]
    public async Task EndOfFile_raises_OnCompletion_once()
    {
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(new FakeBridge(), factory));
        await engine.Inner.LoadAsync(TrackSnapshot.Test("song", "vid-1", 30));

        var completed = 0;
        engine.Inner.OnCompletion = () => completed++;

        factory.LastSession!.RaiseEndOfFile();
        factory.LastSession.RaiseEndOfFile();
        await Task.Delay(20);

        Assert.Equal(1, completed);
        Assert.False(engine.Inner.State.IsPlaying);
    }

    [Fact]
    public async Task Overlapping_LoadAsync_cancels_prior_resolve_and_replaces_session()
    {
        var bridge = new FakeBridge { ResolveDelay = TimeSpan.FromMilliseconds(120) };
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(bridge, factory));

        var first = engine.Inner.LoadAsync(TrackSnapshot.Test("a", "vid-a", 60));
        await Task.Delay(20);
        var second = engine.Inner.LoadAsync(TrackSnapshot.Test("b", "vid-b", 60));

        await second;
        try { await first; } catch (OperationCanceledException) { /* expected */ }

        Assert.Equal("b", engine.Inner.State.Track?.Title);
        Assert.Contains(factory.StartedUrls, u => u.AbsoluteUri.Contains("vid-b", StringComparison.Ordinal));
        Assert.True(factory.LastSession!.IsAlive);
        Assert.Contains("vid-b", factory.LastSession.Url.AbsoluteUri, StringComparison.Ordinal);
        Assert.True(engine.Inner.State.IsPlaying);
    }

    [Fact]
    public async Task Missing_mpv_sets_EngineStartFailed()
    {
        var factory = new FakeMpvFactory { BinaryPath = null };
        await using var engine = new EngineOwner(new ProcessStreamEngine(new FakeBridge(), factory));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.Inner.LoadAsync(TrackSnapshot.Test("song", "vid-1", 10)));

        Assert.Equal(PlayerErrorKind.EngineStartFailed, engine.Inner.State.Error?.Kind);
        Assert.False(engine.Inner.State.IsPlaying);
        Assert.False(engine.Inner.State.Buffering);
    }

    [Fact]
    public async Task Position_comes_from_session_not_wall_clock_while_paused()
    {
        var factory = new FakeMpvFactory();
        await using var engine = new EngineOwner(new ProcessStreamEngine(new FakeBridge(), factory));
        await engine.Inner.LoadAsync(TrackSnapshot.Test("song", "vid-1", 120));

        factory.LastSession!.SetTimePos(15);
        engine.Inner.Pause();
        var pausedAt = engine.Inner.State.Position;
        await Task.Delay(250);
        // While paused the wall clock must not advance position.
        Assert.Equal(pausedAt, engine.Inner.State.Position);
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

    private sealed class FakeBridge : IYTDlpBridge
    {
        public int ResolveCount { get; private set; }
        public TimeSpan ResolveDelay { get; set; } = TimeSpan.Zero;

        public async Task<Uri> ResolveStreamUrlAsync(string videoId, string quality, TimeSpan timeout, CancellationToken ct = default)
        {
            ResolveCount++;
            if (ResolveDelay > TimeSpan.Zero)
                await Task.Delay(ResolveDelay, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new Uri($"https://example.com/stream/{videoId}.m4a");
        }

        public Task<IReadOnlyList<YTDlpPlaylistEntry>> FetchPlaylistAsync(string url, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<YTDlpPlaylistEntry>>([]);

        public Task<IReadOnlyList<YTDlpPlaylistEntry>> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<YTDlpPlaylistEntry>>([]);

        public Task<YTDlpPlaylistEntry?> FetchVideoMetadataAsync(string videoId, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<YTDlpPlaylistEntry?>(null);

        public Task<string?> VersionAsync() => Task.FromResult<string?>("fake");
    }

    private sealed class FakeMpvFactory : IMpvPlayerFactory
    {
        public string? BinaryPath { get; set; } = "/fake/mpv";
        public FakeMpvSession? LastSession { get; private set; }
        public List<Uri> StartedUrls { get; } = [];
        public int LiveSessionCount => StartedUrls.Count == 0 ? 0 : (LastSession?.IsAlive == true ? 1 : 0);

        public string? FindBinary() => BinaryPath;

        public IMpvPlayerSession Start(Uri url, int initialVolume0to100)
        {
            if (BinaryPath is null)
                throw new FileNotFoundException("mpv missing");
            StartedUrls.Add(url);
            LastSession?.Dispose();
            LastSession = new FakeMpvSession(url, initialVolume0to100);
            return LastSession;
        }
    }

    private sealed class FakeMpvSession : IMpvPlayerSession
    {
        private int _disposed;
        public FakeMpvSession(Uri url, int initialVolume)
        {
            Url = url;
            LastVolume = initialVolume;
            TimePos = 0;
        }

        public Uri Url { get; }
        public bool IsAlive => _disposed == 0;
        public double TimePos { get; private set; }
        public bool Paused { get; private set; }
        public double? LastSeek { get; private set; }
        public int LastVolume { get; private set; }

        public event Action? EndOfFile;
        public event Action? Exited;

        public void SetPause(bool paused) => Paused = paused;
        public void SeekAbsolute(double seconds)
        {
            LastSeek = seconds;
            TimePos = seconds;
        }
        public void SetVolume(int volume0to100) => LastVolume = volume0to100;
        public string? LastAf { get; private set; }
        public void SetAudioFilters(string? afChain) => LastAf = afChain ?? "";
        public void SetTimePos(double seconds) => TimePos = seconds;
        public Task<double?> GetTimePosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<double?>(TimePos);

        public void RaiseEndOfFile() => EndOfFile?.Invoke();
        public void RaiseExited() => Exited?.Invoke();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { Exited?.Invoke(); } catch { /* ignore */ }
        }
    }
}
