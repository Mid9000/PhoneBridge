using System.Buffers.Binary;
using System.Text;

namespace PhoneBridge.Bluetooth.Platform;

/// <summary>SDP data elements (Core spec Vol 3 Part B §3), enough to publish the MNS record and read MAS/PBAP records.</summary>
public sealed record SdpElement(int Type, ulong? Number = null, byte[]? Bytes = null, string? Text = null, IReadOnlyList<SdpElement>? Items = null)
{
    public const int TypeNil = 0, TypeUInt = 1, TypeInt = 2, TypeUuid = 3, TypeText = 4, TypeBool = 5, TypeSequence = 6, TypeAlternative = 7, TypeUrl = 8;

    public override string ToString() => Type switch
    {
        TypeUInt or TypeInt or TypeBool => $"0x{Number:X}",
        TypeUuid => Bytes!.Length <= 4 ? $"uuid 0x{Number:X4}" : $"uuid {Convert.ToHexString(Bytes)}",
        TypeText or TypeUrl => $"\"{Text}\"",
        TypeSequence or TypeAlternative => "{" + string.Join(", ", Items!) + "}",
        _ => "nil",
    };
}

public static class Sdp
{
    public static byte[] UInt8(byte v) => [0x08, v];
    public static byte[] UInt16(ushort v) => [0x09, (byte)(v >> 8), (byte)v];
    public static byte[] UInt32(uint v) => [0x0A, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    public static byte[] Uuid16(ushort v) => [0x19, (byte)(v >> 8), (byte)v];

    public static byte[] Text(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        if (b.Length > 255) throw new ArgumentException("SDP text too long", nameof(s));
        return [0x25, (byte)b.Length, .. b];
    }

    public static byte[] Sequence(params byte[][] items)
    {
        var body = items.SelectMany(i => i).ToArray();
        if (body.Length > 255) throw new ArgumentException("SDP sequence too long");
        return [0x35, (byte)body.Length, .. body];
    }

    /// <summary>BluetoothProfileDescriptorList (attribute 0x0009): {{profile UUID, version}}.</summary>
    public static byte[] ProfileDescriptorList(ushort profileUuid, ushort version) =>
        Sequence(Sequence(Uuid16(profileUuid), UInt16(version)));

    public static SdpElement Parse(ReadOnlySpan<byte> data) => Parse(data, out _);

    public static SdpElement Parse(ReadOnlySpan<byte> data, out int consumed)
    {
        if (data.IsEmpty) throw new FormatException("Empty SDP element");
        int type = data[0] >> 3, sizeIndex = data[0] & 0x07;
        int header = 1, length;
        switch (sizeIndex)
        {
            case <= 4:
                length = type == SdpElement.TypeNil ? 0 : 1 << sizeIndex;
                break;
            case 5: Need(data, 2); length = data[1]; header = 2; break;
            case 6: Need(data, 3); length = BinaryPrimitives.ReadUInt16BigEndian(data[1..]); header = 3; break;
            default:
                Need(data, 5);
                uint l = BinaryPrimitives.ReadUInt32BigEndian(data[1..]);
                if (l > int.MaxValue) throw new FormatException("SDP element too large");
                length = (int)l; header = 5;
                break;
        }
        Need(data, header + length);
        var value = data.Slice(header, length);
        consumed = header + length;

        switch (type)
        {
            case SdpElement.TypeUInt:
            case SdpElement.TypeInt:
            case SdpElement.TypeBool:
            case SdpElement.TypeUuid when length <= 8:
                ulong n = 0;
                foreach (var b in value.Length <= 8 ? value : value[^8..]) n = (n << 8) | b;
                return new SdpElement(type, n, value.ToArray());
            case SdpElement.TypeUuid:
                return new SdpElement(type, Bytes: value.ToArray());
            case SdpElement.TypeText:
            case SdpElement.TypeUrl:
                return new SdpElement(type, Text: Encoding.UTF8.GetString(value).TrimEnd('\0'), Bytes: value.ToArray());
            case SdpElement.TypeSequence:
            case SdpElement.TypeAlternative:
                var items = new List<SdpElement>();
                int i = 0;
                while (i < value.Length)
                {
                    items.Add(Parse(value[i..], out int used));
                    i += used;
                }
                return new SdpElement(type, Items: items);
            default:
                return new SdpElement(type, Bytes: value.ToArray());
        }
    }

    private static void Need(ReadOnlySpan<byte> data, int n)
    {
        if (data.Length < n) throw new FormatException("Truncated SDP element");
    }

    /// <summary>Known attribute names, for diagnostics only.</summary>
    public static string AttributeName(uint id) => id switch
    {
        0x0001 => "ServiceClassIDList",
        0x0004 => "ProtocolDescriptorList",
        0x0009 => "BluetoothProfileDescriptorList",
        0x0100 => "ServiceName",
        0x0200 => "GoepL2capPsm",
        0x0314 => "SupportedRepositories(PBAP)",
        0x0315 => "MASInstanceID",
        0x0316 => "SupportedMessageTypes",
        0x0317 => "SupportedFeatures(MAP/PBAP)",
        _ => $"0x{id:X4}",
    };
}
