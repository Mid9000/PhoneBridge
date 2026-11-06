using System.Buffers.Binary;
using System.Text;

namespace PhoneBridge.Bluetooth.Obex;

/// <summary>IrOBEX 1.2 request opcodes. The high bit is the "final" flag.</summary>
public static class ObexOpcode
{
    public const byte Connect = 0x80;
    public const byte Disconnect = 0x81;
    public const byte Put = 0x02;
    public const byte Get = 0x03;
    public const byte SetPath = 0x85;
    public const byte Abort = 0xFF;
    public const byte FinalBit = 0x80;
}

/// <summary>Response codes without the final bit (on the wire: code | 0x80).</summary>
public enum ObexResponseCode : byte
{
    Continue = 0x10,
    Success = 0x20,
    Created = 0x21,
    Accepted = 0x22,
    NoContent = 0x24,
    BadRequest = 0x40,
    Unauthorized = 0x41,
    Forbidden = 0x43,
    NotFound = 0x44,
    MethodNotAllowed = 0x45,
    NotAcceptable = 0x46,
    RequestTimeout = 0x48,
    Conflict = 0x49,
    Gone = 0x4A,
    PreconditionFailed = 0x4C,
    EntityTooLarge = 0x4D,
    UnsupportedMediaType = 0x4F,
    InternalServerError = 0x50,
    NotImplemented = 0x51,
    ServiceUnavailable = 0x53,
    DatabaseFull = 0x60,
    DatabaseLocked = 0x61,
}

public static class ObexHeaderId
{
    // High 2 bits give the encoding: 00 UTF-16BE text, 01 bytes, 10 one byte, 11 four bytes.
    public const byte Count = 0xC0;
    public const byte Name = 0x01;
    public const byte Type = 0x42;
    public const byte Length = 0xC3;
    public const byte Time = 0x44;
    public const byte Description = 0x05;
    public const byte Target = 0x46;
    public const byte Body = 0x48;
    public const byte EndOfBody = 0x49;
    public const byte Who = 0x4A;
    public const byte ConnectionId = 0xCB;
    public const byte AppParameters = 0x4C;
    public const byte AuthChallenge = 0x4D;
    public const byte AuthResponse = 0x4E;
}

public sealed class ObexHeader
{
    public byte Id { get; }
    /// <summary>Raw value bytes, as on the wire after the length field (text includes its null terminator).</summary>
    public byte[] Value { get; }

    public ObexHeader(byte id, byte[] value) { Id = id; Value = value; }

    public static ObexHeader Text(byte id, string? text) =>
        new(id, string.IsNullOrEmpty(text) ? [] : [.. Encoding.BigEndianUnicode.GetBytes(text), 0, 0]);

    public static ObexHeader Bytes(byte id, byte[] data) => new(id, data);
    public static ObexHeader Byte(byte id, byte b) => new(id, [b]);

    public static ObexHeader UInt32(byte id, uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return new(id, b);
    }

    public static ObexHeader Name(string? name) => Text(ObexHeaderId.Name, name);
    /// <summary>Type is null-terminated ASCII.</summary>
    public static ObexHeader Type(string mime) => new(ObexHeaderId.Type, [.. Encoding.ASCII.GetBytes(mime), 0]);
    public static ObexHeader ConnectionId(uint id) => UInt32(ObexHeaderId.ConnectionId, id);

    public string AsText() =>
        (Id & 0xC0) == 0x00
            ? Encoding.BigEndianUnicode.GetString(Value).TrimEnd('\0')
            : Encoding.ASCII.GetString(Value).TrimEnd('\0');

    public uint AsUInt32() => Value.Length == 4 ? BinaryPrimitives.ReadUInt32BigEndian(Value) : Value.Length == 1 ? Value[0] : 0u;

    public int EncodedLength => (Id & 0xC0) switch { 0x80 => 2, 0xC0 => 5, _ => 3 + Value.Length };

    public void WriteTo(List<byte> output)
    {
        output.Add(Id);
        switch (Id & 0xC0)
        {
            case 0x80:
                output.Add(Value.Length > 0 ? Value[0] : (byte)0);
                break;
            case 0xC0:
                if (Value.Length != 4) throw new InvalidOperationException($"Header 0x{Id:X2} needs 4 bytes");
                output.AddRange(Value);
                break;
            default:
                int len = 3 + Value.Length;
                if (len > 0xFFFF) throw new InvalidOperationException("Header too long");
                output.Add((byte)(len >> 8));
                output.Add((byte)len);
                output.AddRange(Value);
                break;
        }
    }

    public override string ToString() => $"0x{Id:X2}[{Value.Length}]";
}

public sealed class ObexHeaderList : List<ObexHeader>
{
    public ObexHeaderList() { }
    public ObexHeaderList(IEnumerable<ObexHeader> items) : base(items) { }

    public ObexHeader? Find(byte id) => this.FirstOrDefault(h => h.Id == id);
    public string? GetText(byte id) => Find(id)?.AsText();
    public uint? GetUInt32(byte id) => Find(id)?.AsUInt32();
    public byte[]? GetBytes(byte id) => Find(id)?.Value;

    public int EncodedLength => this.Sum(h => h.EncodedLength);

    /// <summary>Parses a header block. Throws ObexProtocolException on truncated or malformed data.</summary>
    public static ObexHeaderList Parse(ReadOnlySpan<byte> data)
    {
        var list = new ObexHeaderList();
        int i = 0;
        while (i < data.Length)
        {
            byte id = data[i];
            switch (id & 0xC0)
            {
                case 0x80:
                    if (i + 2 > data.Length) throw new ObexProtocolException("Truncated 1-byte header");
                    list.Add(new ObexHeader(id, [data[i + 1]]));
                    i += 2;
                    break;
                case 0xC0:
                    if (i + 5 > data.Length) throw new ObexProtocolException("Truncated 4-byte header");
                    list.Add(new ObexHeader(id, data.Slice(i + 1, 4).ToArray()));
                    i += 5;
                    break;
                default:
                    if (i + 3 > data.Length) throw new ObexProtocolException("Truncated header length");
                    int len = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 1)..]);
                    if (len < 3 || i + len > data.Length) throw new ObexProtocolException($"Bad header length {len} for 0x{id:X2}");
                    list.Add(new ObexHeader(id, data.Slice(i + 3, len - 3).ToArray()));
                    i += len;
                    break;
            }
        }
        return list;
    }
}

public sealed class ObexProtocolException(string message) : Exception(message);

/// <summary>One OBEX packet: opcode or response byte, an optional fixed prefix (Connect: 4 bytes, SetPath: 2), headers.</summary>
public sealed class ObexPacket
{
    public const int MinimumMaxPacketLength = 255;

    public byte Code { get; }
    public byte[] Prefix { get; }
    public ObexHeaderList Headers { get; }

    public ObexPacket(byte code, byte[]? prefix = null, IEnumerable<ObexHeader>? headers = null)
    {
        Code = code;
        Prefix = prefix ?? [];
        Headers = headers is null ? new ObexHeaderList() : new ObexHeaderList(headers);
    }

    public bool IsFinal => (Code & ObexOpcode.FinalBit) != 0;
    public ObexResponseCode ResponseCode => (ObexResponseCode)(Code & 0x7F);

    public int Length => 3 + Prefix.Length + Headers.EncodedLength;

    public byte[] Encode()
    {
        var output = new List<byte>(Length) { Code, 0, 0 };
        output.AddRange(Prefix);
        foreach (var h in Headers) h.WriteTo(output);
        if (output.Count > 0xFFFF) throw new InvalidOperationException("Packet too long");
        output[1] = (byte)(output.Count >> 8);
        output[2] = (byte)output.Count;
        return [.. output];
    }

    public static ObexPacket Decode(ReadOnlySpan<byte> data, int prefixLength)
    {
        if (data.Length < 3) throw new ObexProtocolException("Packet shorter than 3 bytes");
        int len = BinaryPrimitives.ReadUInt16BigEndian(data[1..]);
        if (len != data.Length) throw new ObexProtocolException($"Length field {len} != {data.Length}");
        if (len < 3 + prefixLength) throw new ObexProtocolException("Packet shorter than its prefix");
        var prefix = data.Slice(3, prefixLength).ToArray();
        return new ObexPacket(data[0], prefix, ObexHeaderList.Parse(data[(3 + prefixLength)..]));
    }

    /// <summary>Request prefix length by opcode (requests only; Connect responses also carry 4 bytes).</summary>
    public static int RequestPrefixLength(byte opcode) => opcode switch
    {
        ObexOpcode.Connect => 4,
        ObexOpcode.SetPath => 2,
        _ => 0,
    };

    /// <summary>Reads one whole packet (3-byte head then the rest). Returns null on clean end of stream.</summary>
    public static async Task<byte[]?> ReadRawAsync(Stream stream, CancellationToken ct)
    {
        var head = new byte[3];
        int got = 0;
        while (got < 3)
        {
            int n = await stream.ReadAsync(head.AsMemory(got), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (got == 0) return null;
                throw new EndOfStreamException("Stream ended inside an OBEX packet");
            }
            got += n;
        }
        int len = (head[1] << 8) | head[2];
        if (len < 3) throw new ObexProtocolException($"Invalid packet length {len}");
        var packet = new byte[len];
        head.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(3), ct).ConfigureAwait(false);
        return packet;
    }

    public static byte[] ConnectPrefix(ushort maxPacketLength) =>
        [0x10, 0x00, (byte)(maxPacketLength >> 8), (byte)maxPacketLength];
}
