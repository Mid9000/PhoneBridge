namespace PhoneBridge.Bluetooth.Obex;

/// <summary>A complete request after multi-packet reassembly.</summary>
public sealed class ObexServerRequest
{
    public byte Opcode { get; init; }          // without the final bit
    public byte[] Prefix { get; init; } = [];  // Connect: version/flags/maxlen; SetPath: flags/constants
    public ObexHeaderList Headers { get; init; } = new();
    public byte[]? Body { get; init; }         // PUT only; null when no Body/EndOfBody header was sent
}

public sealed class ObexServerReply
{
    public ObexResponseCode Code { get; init; } = ObexResponseCode.Success;
    public ObexHeaderList Headers { get; init; } = new();
    public byte[]? Body { get; init; }         // GET only

    public static ObexServerReply Ok(params ObexHeader[] headers) => new() { Headers = new(headers) };
    public static ObexServerReply Error(ObexResponseCode code) => new() { Code = code };
}

/// <summary>
/// Minimal OBEX server loop for one connection: reassembles multi-packet PUTs, splits GET bodies
/// across Continue responses. Used for MNS (iPhone pushes event reports to us) and for the test fakes.
/// </summary>
public sealed class ObexServer
{
    private readonly Stream _stream;
    private readonly Func<ObexServerRequest, CancellationToken, Task<ObexServerReply>> _handler;
    private readonly Action<string>? _log;
    private int _clientMax = ObexPacket.MinimumMaxPacketLength;

    public ushort LocalMaxPacketLength { get; init; } = 0xFFFE;

    public ObexServer(Stream stream, Func<ObexServerRequest, CancellationToken, Task<ObexServerReply>> handler, Action<string>? log = null)
    {
        _stream = stream;
        _handler = handler;
        _log = log;
    }

    /// <summary>Runs until the peer disconnects (OBEX Disconnect or end of stream).</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        ObexHeaderList? putHeaders = null;
        MemoryStream? putBody = null;

        while (!ct.IsCancellationRequested)
        {
            var raw = await ObexPacket.ReadRawAsync(_stream, ct).ConfigureAwait(false);
            if (raw is null) return;
            byte opcode = raw[0];
            byte baseOp = opcode == ObexOpcode.Abort ? opcode : (byte)(opcode & 0x7F);
            var packet = ObexPacket.Decode(raw, ObexPacket.RequestPrefixLength(opcode));

            switch (baseOp)
            {
                case ObexOpcode.Connect & 0x7F:
                {
                    if (packet.Prefix.Length == 4)
                        _clientMax = Math.Max(ObexPacket.MinimumMaxPacketLength, (packet.Prefix[2] << 8) | packet.Prefix[3]);
                    var reply = await _handler(new ObexServerRequest { Opcode = ObexOpcode.Connect, Prefix = packet.Prefix, Headers = packet.Headers }, ct).ConfigureAwait(false);
                    await SendAsync(new ObexPacket(Final(reply.Code), ObexPacket.ConnectPrefix(LocalMaxPacketLength), reply.Headers), ct).ConfigureAwait(false);
                    break;
                }
                case ObexOpcode.Disconnect & 0x7F:
                {
                    var reply = await _handler(new ObexServerRequest { Opcode = ObexOpcode.Disconnect, Headers = packet.Headers }, ct).ConfigureAwait(false);
                    await SendAsync(new ObexPacket(Final(reply.Code), null, reply.Headers), ct).ConfigureAwait(false);
                    return;
                }
                case ObexOpcode.SetPath & 0x7F:
                {
                    var reply = await _handler(new ObexServerRequest { Opcode = ObexOpcode.SetPath, Prefix = packet.Prefix, Headers = packet.Headers }, ct).ConfigureAwait(false);
                    await SendAsync(new ObexPacket(Final(reply.Code), null, reply.Headers), ct).ConfigureAwait(false);
                    break;
                }
                case ObexOpcode.Put:
                {
                    putHeaders ??= new ObexHeaderList();
                    foreach (var h in packet.Headers)
                    {
                        if (h.Id is ObexHeaderId.Body or ObexHeaderId.EndOfBody) (putBody ??= new MemoryStream()).Write(h.Value);
                        else putHeaders.Add(h);
                    }
                    if (!packet.IsFinal)
                    {
                        await SendAsync(new ObexPacket(Final(ObexResponseCode.Continue)), ct).ConfigureAwait(false);
                        break;
                    }
                    var request = new ObexServerRequest { Opcode = ObexOpcode.Put, Headers = putHeaders, Body = putBody?.ToArray() };
                    putHeaders = null;
                    putBody = null;
                    var reply = await _handler(request, ct).ConfigureAwait(false);
                    await SendAsync(new ObexPacket(Final(reply.Code), null, reply.Headers), ct).ConfigureAwait(false);
                    break;
                }
                case ObexOpcode.Get:
                {
                    // shortcut: assumes all GET request headers fit in the first (final) packet, true for MAP/PBAP clients.
                    var reply = await _handler(new ObexServerRequest { Opcode = ObexOpcode.Get, Headers = packet.Headers }, ct).ConfigureAwait(false);
                    await SendGetReplyAsync(reply, ct).ConfigureAwait(false);
                    break;
                }
                case ObexOpcode.Abort:
                    putHeaders = null;
                    putBody = null;
                    await SendAsync(new ObexPacket(Final(ObexResponseCode.Success)), ct).ConfigureAwait(false);
                    break;
                default:
                    _log?.Invoke($"OBEX server: unsupported opcode 0x{opcode:X2}");
                    await SendAsync(new ObexPacket(Final(ObexResponseCode.NotImplemented)), ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task SendGetReplyAsync(ObexServerReply reply, CancellationToken ct)
    {
        if (reply.Code != ObexResponseCode.Success || reply.Body is null)
        {
            await SendAsync(new ObexPacket(Final(reply.Code), null, reply.Headers), ct).ConfigureAwait(false);
            return;
        }

        var body = reply.Body;
        int offset = 0;
        var headers = new ObexHeaderList(reply.Headers);
        while (true)
        {
            int room = _clientMax - 3 - headers.EncodedLength - 3;
            if (room < 0) throw new InvalidOperationException("GET reply headers exceed client max packet length");
            int chunk = Math.Min(room, body.Length - offset);
            bool last = offset + chunk == body.Length;
            headers.Add(ObexHeader.Bytes(last ? ObexHeaderId.EndOfBody : ObexHeaderId.Body, body.AsSpan(offset, chunk).ToArray()));
            offset += chunk;
            await SendAsync(new ObexPacket(Final(last ? ObexResponseCode.Success : ObexResponseCode.Continue), null, headers), ct).ConfigureAwait(false);
            if (last) return;
            headers = new ObexHeaderList();

            var next = await ObexPacket.ReadRawAsync(_stream, ct).ConfigureAwait(false) ?? throw new EndOfStreamException("Client left during GET");
            if (next[0] == ObexOpcode.Abort)
            {
                await SendAsync(new ObexPacket(Final(ObexResponseCode.Success)), ct).ConfigureAwait(false);
                return;
            }
            if ((next[0] & 0x7F) != ObexOpcode.Get) throw new ObexProtocolException($"Expected GET continuation, got 0x{next[0]:X2}");
        }
    }

    private static byte Final(ObexResponseCode code) => (byte)((byte)code | 0x80);

    private async Task SendAsync(ObexPacket packet, CancellationToken ct)
    {
        await _stream.WriteAsync(packet.Encode(), ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
