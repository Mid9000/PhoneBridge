namespace PhoneBridge.Bluetooth.Obex;

public sealed class ObexResponse
{
    public ObexResponseCode Code { get; init; }
    public ObexHeaderList Headers { get; init; } = new();
    /// <summary>Concatenated Body/EndOfBody of every packet of the operation.</summary>
    public byte[] Body { get; init; } = [];
    /// <summary>Connect response only: peer's maximum packet length.</summary>
    public int? MaxPacketLength { get; init; }

    public bool IsSuccess => Code == ObexResponseCode.Success;
}

public sealed class ObexException(ObexResponseCode code, string operation)
    : Exception($"OBEX {operation} failed: {code} (0x{(byte)code | 0x80:X2})")
{
    public ObexResponseCode Code { get; } = code;
}

/// <summary>Minimal OBEX client over any duplex byte stream (RFCOMM socket, test loopback). One operation at a time.</summary>
public sealed class ObexClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ushort LocalMaxPacketLength { get; }
    public int PeerMaxPacketLength { get; private set; } = ObexPacket.MinimumMaxPacketLength;
    public uint? ConnectionId { get; private set; }
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public ObexClient(Stream stream, Action<string>? log = null, ushort localMaxPacketLength = 0xFFFE)
    {
        _stream = stream;
        _log = log;
        LocalMaxPacketLength = localMaxPacketLength;
    }

    public async Task<ObexResponse> ConnectAsync(byte[]? target, IEnumerable<ObexHeader>? extraHeaders = null, CancellationToken ct = default)
    {
        var headers = new ObexHeaderList();
        if (target is not null) headers.Add(ObexHeader.Bytes(ObexHeaderId.Target, target));
        if (extraHeaders is not null) headers.AddRange(extraHeaders);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var reply = await ExchangeAsync(new ObexPacket(ObexOpcode.Connect, ObexPacket.ConnectPrefix(LocalMaxPacketLength), headers), 4, ct).ConfigureAwait(false);
            int? max = null;
            if (reply.Prefix.Length == 4)
            {
                max = (reply.Prefix[2] << 8) | reply.Prefix[3];
                if (max >= ObexPacket.MinimumMaxPacketLength) PeerMaxPacketLength = max.Value;
            }
            ConnectionId = reply.Headers.GetUInt32(ObexHeaderId.ConnectionId);
            _log?.Invoke($"OBEX connect -> {reply.ResponseCode}, peerMax={PeerMaxPacketLength}, connId={(ConnectionId.HasValue ? "yes" : "no")}");
            return new ObexResponse { Code = reply.ResponseCode, Headers = reply.Headers, MaxPacketLength = max };
        }
        finally { _gate.Release(); }
    }

    public async Task<ObexResponse> DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var reply = await ExchangeAsync(new ObexPacket(ObexOpcode.Disconnect, null, WithConnectionId([])), 0, ct).ConfigureAwait(false);
            ConnectionId = null;
            return new ObexResponse { Code = reply.ResponseCode, Headers = reply.Headers };
        }
        finally { _gate.Release(); }
    }

    /// <summary>SetPath. name == "" goes to root, up == true goes to parent (name ignored), otherwise into child "name".</summary>
    public async Task<ObexResponse> SetPathAsync(string name, bool up = false, bool create = false, CancellationToken ct = default)
    {
        byte flags = (byte)((up ? 0x01 : 0) | (create ? 0 : 0x02));
        var headers = WithConnectionId(up ? [] : [ObexHeader.Name(name)]);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var reply = await ExchangeAsync(new ObexPacket(ObexOpcode.SetPath, [flags, 0x00], headers), 0, ct).ConfigureAwait(false);
            return new ObexResponse { Code = reply.ResponseCode, Headers = reply.Headers };
        }
        finally { _gate.Release(); }
    }

    /// <summary>GET; follows Continue responses and accumulates the body.</summary>
    public async Task<ObexResponse> GetAsync(IEnumerable<ObexHeader> headers, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var first = new ObexPacket(ObexOpcode.Get | ObexOpcode.FinalBit, null, WithConnectionId(headers));
            if (first.Length > PeerMaxPacketLength) throw new InvalidOperationException("GET headers exceed peer max packet length");

            var body = new MemoryStream();
            var allHeaders = new ObexHeaderList();
            var reply = await ExchangeAsync(first, 0, ct).ConfigureAwait(false);
            int packets = 1;
            while (true)
            {
                foreach (var h in reply.Headers)
                {
                    if (h.Id is ObexHeaderId.Body or ObexHeaderId.EndOfBody) body.Write(h.Value);
                    else allHeaders.Add(h);
                }
                if (reply.ResponseCode != ObexResponseCode.Continue) break;
                reply = await ExchangeAsync(new ObexPacket(ObexOpcode.Get | ObexOpcode.FinalBit), 0, ct).ConfigureAwait(false);
                packets++;
            }
            _log?.Invoke($"OBEX get -> {reply.ResponseCode}, {packets} packet(s), {body.Length} byte(s)");
            return new ObexResponse { Code = reply.ResponseCode, Headers = allHeaders, Body = body.ToArray() };
        }
        finally { _gate.Release(); }
    }

    /// <summary>PUT; splits the body to fit the peer's max packet length. body == null sends no body headers.</summary>
    public async Task<ObexResponse> PutAsync(IEnumerable<ObexHeader> headers, byte[]? body, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = WithConnectionId(headers);
            int offset = 0;
            var allHeaders = new ObexHeaderList();
            while (true)
            {
                int room = PeerMaxPacketLength - 3 - pending.EncodedLength - 3;
                if (room < 0) throw new InvalidOperationException("PUT headers exceed peer max packet length");
                var packetHeaders = new ObexHeaderList(pending);
                pending = new ObexHeaderList();
                bool final;
                if (body is null) final = true;
                else
                {
                    int remaining = body.Length - offset;
                    int chunk = Math.Min(room, remaining);
                    final = chunk == remaining;
                    packetHeaders.Add(ObexHeader.Bytes(final ? ObexHeaderId.EndOfBody : ObexHeaderId.Body, body.AsSpan(offset, chunk).ToArray()));
                    offset += chunk;
                }

                var reply = await ExchangeAsync(new ObexPacket(final ? (byte)(ObexOpcode.Put | ObexOpcode.FinalBit) : ObexOpcode.Put, null, packetHeaders), 0, ct).ConfigureAwait(false);
                allHeaders.AddRange(reply.Headers);
                if (final || reply.ResponseCode != ObexResponseCode.Continue)
                {
                    _log?.Invoke($"OBEX put -> {reply.ResponseCode}, {offset} byte(s) sent");
                    return new ObexResponse { Code = reply.ResponseCode, Headers = allHeaders };
                }
            }
        }
        finally { _gate.Release(); }
    }

    private ObexHeaderList WithConnectionId(IEnumerable<ObexHeader> headers)
    {
        var list = new ObexHeaderList();
        // ConnectionId must be the first header of the operation.
        if (ConnectionId is uint id) list.Add(ObexHeader.ConnectionId(id));
        list.AddRange(headers);
        return list;
    }

    private async Task<ObexPacket> ExchangeAsync(ObexPacket request, int responsePrefixLength, CancellationToken ct)
    {
        var bytes = request.Encode();
        await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ResponseTimeout);
        byte[]? raw;
        try { raw = await ObexPacket.ReadRawAsync(_stream, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No OBEX response within {ResponseTimeout.TotalSeconds:0}s");
        }
        if (raw is null) throw new EndOfStreamException("OBEX peer closed the connection");
        // A non-success Connect response may omit the 4-byte prefix.
        int prefix = raw.Length >= 3 + responsePrefixLength ? responsePrefixLength : 0;
        return ObexPacket.Decode(raw, prefix);
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
