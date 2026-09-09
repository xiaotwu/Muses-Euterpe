using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Muses.Infrastructure.Playback;

/// <summary>
/// Minimal mpv JSON IPC client over a UNIX domain socket (POSIX) or named pipe (Windows).
/// </summary>
internal sealed class MpvIpcClient : IDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pending = new();
    private int _nextRequestId;
    private Stream? _stream;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private int _disposed;

    public event Action<string, JsonElement>? EventReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync(string endpoint, bool windowsNamedPipe, CancellationToken cancellationToken)
    {
        if (windowsNamedPipe)
        {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
            _stream = pipe;
        }
        else
        {
            // Wait briefly for mpv to create the socket file.
            for (var i = 0; i < 80; i++)
            {
                if (File.Exists(endpoint)) break;
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(endpoint))
                throw new IOException($"mpv IPC socket was not created: {endpoint}");

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), cancellationToken).ConfigureAwait(false);
            _stream = new NetworkStream(socket, ownsSocket: true);
        }

        _writer = new StreamWriter(_stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
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
            await _writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
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
                return null;
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
                    if (_pending.TryRemove(id, out var tcs))
                        tcs.TrySetResult(doc);
                    else
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
                    tcs.TrySetCanceled();
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
