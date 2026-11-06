using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace PhoneBridge.Server;

/// <summary>Connected /v1/events sockets. Server -> iPhone push only; incoming frames are ignored.</summary>
public sealed class EventHub
{
    readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _sockets = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int Count => _sockets.Count;

    public async Task RunAsync(WebSocket socket, object? hello, CancellationToken ct)
    {
        var gate = new SemaphoreSlim(1, 1);
        _sockets[socket] = gate;
        try
        {
            if (hello is not null) await SendAsync(socket, gate, hello, ct);
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var r = await socket.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
        finally
        {
            _sockets.TryRemove(socket, out _);
        }
    }

    public void Broadcast(object message)
    {
        foreach (var (socket, gate) in _sockets)
            _ = SendAsync(socket, gate, message, CancellationToken.None);
    }

    static async Task SendAsync(WebSocket socket, SemaphoreSlim gate, object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), Json);
        await gate.WaitAsync(ct);
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
        finally { gate.Release(); }
    }
}
