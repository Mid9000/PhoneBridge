using System.Text;
using PhoneBridge.Bluetooth.Obex;

namespace PhoneBridge.Bluetooth.Map;

/// <summary>
/// MAP Message Notification Server: the iPhone connects to us (after SetNotificationRegistration on) and PUTs
/// x-bt/MAP-event-report objects. Transport-agnostic: call RunConnectionAsync for each accepted stream.
/// </summary>
public sealed class MnsServer
{
    private readonly Action<string>? _log;
    private uint _nextConnectionId = 1;

    public event EventHandler<MapEvent>? EventReceived;

    public MnsServer(Action<string>? log = null) => _log = log;

    public async Task RunConnectionAsync(Stream stream, CancellationToken ct = default)
    {
        var server = new ObexServer(stream, HandleAsync, _log);
        try { await server.RunAsync(ct).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or EndOfStreamException or ObexProtocolException)
        {
            _log?.Invoke($"MNS connection ended: {e.GetType().Name}");
        }
    }

    internal Task<ObexServerReply> HandleAsync(ObexServerRequest request, CancellationToken ct)
    {
        switch (request.Opcode)
        {
            case ObexOpcode.Connect:
                var target = request.Headers.GetBytes(ObexHeaderId.Target);
                if (target is null || !target.AsSpan().SequenceEqual(BluetoothIds.MnsTarget))
                {
                    _log?.Invoke("MNS: connect with wrong target refused");
                    return Task.FromResult(ObexServerReply.Error(ObexResponseCode.NotAcceptable));
                }
                _log?.Invoke("MNS: iPhone connected");
                return Task.FromResult(ObexServerReply.Ok(
                    ObexHeader.ConnectionId(_nextConnectionId++),
                    ObexHeader.Bytes(ObexHeaderId.Who, BluetoothIds.MnsTarget)));

            case ObexOpcode.Put:
                var type = request.Headers.GetText(ObexHeaderId.Type);
                if (!string.Equals(type, MapTypes.EventReport, StringComparison.OrdinalIgnoreCase) || request.Body is null)
                    return Task.FromResult(ObexServerReply.Error(ObexResponseCode.BadRequest));
                var events = MapXml.ParseEventReport(Encoding.UTF8.GetString(request.Body));
                foreach (var e in events)
                {
                    _log?.Invoke($"MNS: event {e.Type} folder={e.Folder} type={e.MessageType}");
                    EventReceived?.Invoke(this, e);
                }
                return Task.FromResult(ObexServerReply.Ok());

            case ObexOpcode.Disconnect:
                _log?.Invoke("MNS: iPhone disconnected");
                return Task.FromResult(ObexServerReply.Ok());

            default:
                return Task.FromResult(ObexServerReply.Error(ObexResponseCode.NotImplemented));
        }
    }
}
