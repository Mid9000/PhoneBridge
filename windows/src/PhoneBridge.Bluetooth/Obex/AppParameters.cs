using System.Buffers.Binary;
using System.Text;

namespace PhoneBridge.Bluetooth.Obex;

/// <summary>OBEX Application Parameters header body: a list of tag(1) length(1) value TLVs, big-endian numbers.</summary>
public sealed class AppParameters
{
    private readonly List<(byte Tag, byte[] Value)> _items = [];

    public IReadOnlyList<(byte Tag, byte[] Value)> Items => _items;

    public AppParameters Add(byte tag, byte[] value)
    {
        if (value.Length > 255) throw new ArgumentException("TLV value longer than 255 bytes", nameof(value));
        _items.Add((tag, value));
        return this;
    }

    public AppParameters AddByte(byte tag, byte v) => Add(tag, [v]);

    public AppParameters AddUInt16(byte tag, ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        return Add(tag, b);
    }

    public AppParameters AddUInt32(byte tag, uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return Add(tag, b);
    }

    public AppParameters AddUInt64(byte tag, ulong v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, v);
        return Add(tag, b);
    }

    public AppParameters AddString(byte tag, string v) => Add(tag, Encoding.UTF8.GetBytes(v));

    public byte[]? Get(byte tag)
    {
        foreach (var (t, v) in _items) if (t == tag) return v;
        return null;
    }

    /// <summary>Reads a 1, 2, 4 or 8 byte unsigned value; null when absent.</summary>
    public ulong? GetNumber(byte tag)
    {
        var v = Get(tag);
        if (v is null || v.Length is 0 or > 8) return null;
        ulong n = 0;
        foreach (var b in v) n = (n << 8) | b;
        return n;
    }

    public byte[] Encode()
    {
        var output = new List<byte>();
        foreach (var (t, v) in _items)
        {
            output.Add(t);
            output.Add((byte)v.Length);
            output.AddRange(v);
        }
        return [.. output];
    }

    public ObexHeader ToHeader() => ObexHeader.Bytes(ObexHeaderId.AppParameters, Encode());

    public static AppParameters Parse(ReadOnlySpan<byte> data)
    {
        var p = new AppParameters();
        int i = 0;
        while (i < data.Length)
        {
            if (i + 2 > data.Length) throw new ObexProtocolException("Truncated application parameter");
            byte tag = data[i];
            int len = data[i + 1];
            if (i + 2 + len > data.Length) throw new ObexProtocolException($"Application parameter 0x{tag:X2} overruns");
            p._items.Add((tag, data.Slice(i + 2, len).ToArray()));
            i += 2 + len;
        }
        return p;
    }

    public static AppParameters? From(ObexHeaderList headers) =>
        headers.GetBytes(ObexHeaderId.AppParameters) is { } b ? Parse(b) : null;
}
