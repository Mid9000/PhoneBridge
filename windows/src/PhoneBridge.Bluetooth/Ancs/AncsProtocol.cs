using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PhoneBridge.Bluetooth.Ancs;

// Values from the ANCS spec appendix (tables 3-1 to 3-7), checked 2026-11-06. All multi-byte fields are little-endian.

public enum AncsEventId : byte { Added = 0, Modified = 1, Removed = 2 }

[Flags]
public enum AncsEventFlags : byte
{
    None = 0,
    Silent = 1 << 0,
    Important = 1 << 1,
    PreExisting = 1 << 2,
    PositiveAction = 1 << 3,
    NegativeAction = 1 << 4,
}

public enum AncsCategory : byte
{
    Other = 0, IncomingCall = 1, MissedCall = 2, Voicemail = 3, Social = 4, Schedule = 5, Email = 6,
    News = 7, HealthAndFitness = 8, BusinessAndFinance = 9, Location = 10, Entertainment = 11,
}

public enum AncsCommandId : byte { GetNotificationAttributes = 0, GetAppAttributes = 1, PerformNotificationAction = 2 }

public enum AncsNotificationAttribute : byte
{
    AppIdentifier = 0, Title = 1, Subtitle = 2, Message = 3, MessageSize = 4, Date = 5,
    PositiveActionLabel = 6, NegativeActionLabel = 7,
}

public enum AncsAppAttribute : byte { DisplayName = 0 }

public enum AncsAction : byte { Positive = 0, Negative = 1 }

public static class AncsErrorCode
{
    public const byte UnknownCommand = 0xA0;
    public const byte InvalidCommand = 0xA1;
    public const byte InvalidParameter = 0xA2;
    public const byte ActionFailed = 0xA3;
}

public readonly record struct AncsNotificationSourceEvent(
    AncsEventId EventId, AncsEventFlags Flags, AncsCategory Category, byte CategoryCount, uint Uid)
{
    public const int Size = 8;

    public static AncsNotificationSourceEvent Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size) throw new FormatException($"Notification Source packet is {data.Length} bytes, expected 8");
        return new((AncsEventId)data[0], (AncsEventFlags)data[1], (AncsCategory)data[2], data[3],
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
    }
}

public readonly record struct AncsAttributeRequest(byte Id, ushort? MaxLength = null);

public static class AncsCommands
{
    /// <summary>Attributes that need a 2-byte max length (Title, Subtitle, Message).</summary>
    public static bool NeedsMaxLength(AncsNotificationAttribute a) =>
        a is AncsNotificationAttribute.Title or AncsNotificationAttribute.Subtitle or AncsNotificationAttribute.Message;

    /// <summary>The set PhoneBridge asks for: limits match BlueFerry's (128 for titles, 1024 for message text).</summary>
    public static readonly AncsAttributeRequest[] DefaultNotificationAttributes =
    [
        new((byte)AncsNotificationAttribute.AppIdentifier),
        new((byte)AncsNotificationAttribute.Title, 128),
        new((byte)AncsNotificationAttribute.Subtitle, 128),
        new((byte)AncsNotificationAttribute.Message, 1024),
        new((byte)AncsNotificationAttribute.Date),
        new((byte)AncsNotificationAttribute.PositiveActionLabel),
        new((byte)AncsNotificationAttribute.NegativeActionLabel),
    ];

    public static byte[] GetNotificationAttributes(uint uid, IReadOnlyList<AncsAttributeRequest> attributes)
    {
        var b = new List<byte> { (byte)AncsCommandId.GetNotificationAttributes };
        Span<byte> u = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u, uid);
        b.AddRange(u.ToArray());
        AppendAttributes(b, attributes, a => NeedsMaxLength((AncsNotificationAttribute)a));
        return [.. b];
    }

    public static byte[] GetAppAttributes(string appIdentifier, IReadOnlyList<AncsAttributeRequest> attributes)
    {
        var b = new List<byte> { (byte)AncsCommandId.GetAppAttributes };
        b.AddRange(Encoding.UTF8.GetBytes(appIdentifier));
        b.Add(0); // AppIdentifier is NULL-terminated in the request.
        AppendAttributes(b, attributes, _ => false);
        return [.. b];
    }

    public static byte[] PerformNotificationAction(uint uid, AncsAction action)
    {
        var b = new byte[6];
        b[0] = (byte)AncsCommandId.PerformNotificationAction;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1), uid);
        b[5] = (byte)action;
        return b;
    }

    private static void AppendAttributes(List<byte> b, IReadOnlyList<AncsAttributeRequest> attributes, Func<byte, bool> needsMax)
    {
        foreach (var a in attributes)
        {
            b.Add(a.Id);
            if (needsMax(a.Id))
            {
                ushort max = a.MaxLength ?? 256;
                b.Add((byte)max);
                b.Add((byte)(max >> 8));
            }
        }
    }

    /// <summary>ANCS Date: "yyyyMMdd'T'HHmmSS" (UTS #35), device local time.</summary>
    public static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
}

public sealed record AncsAttributeResponse(AncsCommandId CommandId, uint? Uid, string? AppIdentifier, IReadOnlyDictionary<byte, string> Attributes);

/// <summary>
/// Reassembles Data Source fragments into one response. The response does not carry its own length, so the
/// reassembler must know which command is pending and how many attributes were requested.
/// </summary>
public sealed class AncsDataSourceReassembler
{
    private readonly List<byte> _buffer = [];
    private AncsCommandId? _command;
    private int _attributeCount;

    public bool IsPending => _command.HasValue;

    public void Expect(AncsCommandId command, int attributeCount)
    {
        _buffer.Clear();
        _command = command;
        _attributeCount = attributeCount;
    }

    public void Reset()
    {
        _buffer.Clear();
        _command = null;
    }

    /// <summary>Appends a fragment; returns the response when complete, null when more data is needed.</summary>
    public AncsAttributeResponse? Append(ReadOnlySpan<byte> fragment)
    {
        if (_command is null) return null; // unsolicited data: ignore
        if (_buffer.Count == 0 && fragment.Length > 0 && fragment[0] != (byte)_command) return null; // stray tail of a timed-out response
        _buffer.AddRange(fragment.ToArray());
        var result = TryParse(_buffer.ToArray(), _command.Value, _attributeCount);
        if (result is not null) Reset();
        return result;
    }

    internal static AncsAttributeResponse? TryParse(ReadOnlySpan<byte> data, AncsCommandId command, int attributeCount)
    {
        if (data.Length < 1 || data[0] != (byte)command) return null;
        int i = 1;
        uint? uid = null;
        string? appId = null;
        if (command == AncsCommandId.GetNotificationAttributes)
        {
            if (data.Length < 5) return null;
            uid = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
            i = 5;
        }
        else
        {
            int nul = data[1..].IndexOf((byte)0);
            if (nul < 0) return null;
            appId = Encoding.UTF8.GetString(data.Slice(1, nul));
            i = 1 + nul + 1;
        }

        var attrs = new Dictionary<byte, string>();
        for (int n = 0; n < attributeCount; n++)
        {
            if (i + 3 > data.Length) return null;
            byte id = data[i];
            int len = BinaryPrimitives.ReadUInt16LittleEndian(data[(i + 1)..]);
            if (i + 3 + len > data.Length) return null;
            attrs[id] = Encoding.UTF8.GetString(data.Slice(i + 3, len));
            i += 3 + len;
        }
        return new AncsAttributeResponse(command, uid, appId, attrs);
    }
}
