using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Muses.Infrastructure.Playback;

/// <summary>
/// Minimal mpv JSON IPC client over a Windows named pipe.
/// </summary>
internal sealed class MpvIpcClient : IDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1);
    private int _nextRequestId;
    private Stream? _stream;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private int _disposed;

    public event Action<string, JsonElement>? EventReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync(string endpoint, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false); }
        catch
        {
            pipe.Dispose();
            throw;
        }
        _stream = pipe;

        _writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        _readCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public async Task<JsonElement?> RequestAsync(object[] command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_writer is null) throw new InvalidOperationException("mpv IPC is not connected.");

        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["command"] = command,
            ["request_id"] = id
        });

        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false); }
            finally { _writeLock.Release(); }
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            using var doc = await tcs.Task.ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("error", out var err) || err.GetString() != "success")
                throw new IOException("mpv rejected the " + command[0] + " command.");
            return doc.RootElement.TryGetProperty("data", out var data) ? data.Clone() : null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task SendAsync(object[] command, CancellationToken cancellationToken = default) =>
        RequestAsync(command, cancellationToken);

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_stream is null) return;
        var reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }

                var root = doc.RootElement;
                if (root.TryGetProperty("request_id", out var rid) && rid.ValueKind == JsonValueKind.Number)
                {
                    var id = rid.GetInt32();
                    if (!_pending.TryRemove(id, out var tcs) || !tcs.TrySetResult(doc))
                        doc.Dispose();
                    continue;
                }

                if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
                {
                    var name = ev.GetString() ?? "";
                    try { EventReceived?.Invoke(name, root.Clone()); }
                    catch { /* listener errors must not kill the read loop */ }
                }

                doc.Dispose();
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            foreach (var kv in _pending)
            {
                if (_pending.TryRemove(kv.Key, out var tcs))
                    tcs.TrySetException(new IOException("mpv IPC disconnected."));
            }
            try { Disconnected?.Invoke(); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _readCts?.Cancel(); } catch { /* ignore */ }
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _readCts?.Dispose(); } catch { /* ignore */ }
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetCanceled();
        }
    }
}
