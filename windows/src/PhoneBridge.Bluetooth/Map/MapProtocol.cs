using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PhoneBridge.Bluetooth.Map;

/// <summary>MAP 1.4 application parameter tags (§6.3.1).</summary>
public static class MapTag
{
    public const byte MaxListCount = 0x01;        // uint16
    public const byte ListStartOffset = 0x02;     // uint16
    public const byte FilterMessageType = 0x03;   // uint8 mask, bit set = excluded
    public const byte FilterPeriodBegin = 0x04;   // string yyyyMMddTHHmmss
    public const byte FilterPeriodEnd = 0x05;
    public const byte FilterReadStatus = 0x06;    // 0 all, 1 unread only, 2 read only
    public const byte FilterRecipient = 0x07;
    public const byte FilterOriginator = 0x08;
    public const byte FilterPriority = 0x09;
    public const byte Attachment = 0x0A;          // 0 off, 1 on
    public const byte Transparent = 0x0B;         // 0 keep a copy in Sent, 1 do not
    public const byte Retry = 0x0C;
    public const byte NewMessage = 0x0D;
    public const byte NotificationStatus = 0x0E;  // 0 off, 1 on
    public const byte MasInstanceId = 0x0F;
    public const byte ParameterMask = 0x10;       // uint32
    public const byte FolderListingSize = 0x11;
    public const byte ListingSize = 0x12;         // MessagesListingSize
    public const byte SubjectLength = 0x13;       // uint8 1..255
    public const byte Charset = 0x14;             // 0 native, 1 UTF-8
    public const byte FractionRequest = 0x15;
    public const byte FractionDeliver = 0x16;
    public const byte StatusIndicator = 0x17;     // 0 read, 1 deleted, 2 extended data
    public const byte StatusValue = 0x18;         // 0 no, 1 yes
    public const byte MseTime = 0x19;
    public const byte MapSupportedFeatures = 0x29; // uint32
}

public static class MapTypes
{
    public const string FolderListing = "x-obex/folder-listing";
    public const string MessagesListing = "x-bt/MAP-msg-listing";
    public const string Message = "x-bt/message";
    public const string MessageStatus = "x-bt/messageStatus";
    public const string NotificationRegistration = "x-bt/MAP-NotificationRegistration";
    public const string EventReport = "x-bt/MAP-event-report";
}

/// <summary>MapSupportedFeatures bits (MAP 1.4 §7.1.1).</summary>
[Flags]
public enum MapFeatures : uint
{
    NotificationRegistration = 1 << 0,
    Notification = 1 << 1,
    Browsing = 1 << 2,
    Uploading = 1 << 3,
    Delete = 1 << 4,
    InstanceInformation = 1 << 5,
    ExtendedEventReport11 = 1 << 6,
    // What we implement as MCE. ExtendedEventReport11 lets the iPhone add subject/sender to events (parser handles 1.0 and 1.1).
    PhoneBridgeClient = NotificationRegistration | Notification | Browsing | Uploading | ExtendedEventReport11,
}

public sealed record MapFolder(string Name);

public sealed record MapMessageEntry
{
    public required string Handle { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? DateTime { get; init; }
    public string? SenderName { get; init; }
    public string? SenderAddressing { get; init; }
    public string? RecipientName { get; init; }
    public string? RecipientAddressing { get; init; }
    public string? Type { get; init; }
    public int? Size { get; init; }
    public bool Read { get; init; }
    public bool Sent { get; init; }
}

public sealed record MapEvent(string Type, string? Handle, string? Folder, string? MessageType,
    DateTimeOffset? DateTime = null, string? Subject = null, string? SenderName = null);

public static class MapXml
{
    private static readonly XmlReaderSettings Safe = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        MaxCharactersInDocument = 16 * 1024 * 1024,
    };

    public static IReadOnlyList<MapFolder> ParseFolderListing(string xml) =>
        Elements(xml, "folder").Select(a => new MapFolder(a.GetValueOrDefault("name") ?? "")).Where(f => f.Name.Length > 0).ToList();

    public static IReadOnlyList<MapMessageEntry> ParseMessagesListing(string xml) =>
        Elements(xml, "msg").Where(a => a.ContainsKey("handle")).Select(a => new MapMessageEntry
        {
            Handle = a["handle"],
            Subject = a.GetValueOrDefault("subject"),
            DateTime = ParseMapTime(a.GetValueOrDefault("datetime")),
            SenderName = a.GetValueOrDefault("sender_name"),
            SenderAddressing = a.GetValueOrDefault("sender_addressing"),
            RecipientName = a.GetValueOrDefault("recipient_name"),
            RecipientAddressing = a.GetValueOrDefault("recipient_addressing"),
            Type = a.GetValueOrDefault("type"),
            Size = int.TryParse(a.GetValueOrDefault("size"), out var s) ? s : null,
            Read = IsYes(a.GetValueOrDefault("read")),
            Sent = IsYes(a.GetValueOrDefault("sent")),
        }).ToList();

    public static IReadOnlyList<MapEvent> ParseEventReport(string xml) =>
        Elements(xml, "event").Select(a => new MapEvent(
            a.GetValueOrDefault("type") ?? "",
            a.GetValueOrDefault("handle"),
            a.GetValueOrDefault("folder"),
            a.GetValueOrDefault("msg_type"),
            ParseMapTime(a.GetValueOrDefault("datetime")),
            a.GetValueOrDefault("subject"),
            a.GetValueOrDefault("sender_name"))).ToList();

    /// <summary>MAP times are "yyyyMMddTHHmmss" with an optional "+hhmm"/"-hhmm" offset; no offset means MSE local time (taken as PC local).</summary>
    public static DateTimeOffset? ParseMapTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.EndsWith('Z')) value = value[..^1] + "+0000";
        string[] formats = ["yyyyMMdd'T'HHmmsszzz", "yyyyMMdd'T'HHmmss"];
        // .NET "zzz" wants +hh:mm, MAP sends +hhmm.
        if (value.Length == 20 && (value[15] == '+' || value[15] == '-')) value = value[..18] + ":" + value[18..];
        if (DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)) return t;
        return null;
    }

    private static bool IsYes(string? v) => string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Attributes of every element named <paramref name="name"/>. Strict XML first; if the MSE sends malformed XML
    /// (unescaped '&amp;' in subjects is a classic), fall back to a tolerant attribute scan.
    /// </summary>
    internal static IEnumerable<Dictionary<string, string>> Elements(string xml, string name)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Safe);
            var doc = XDocument.Load(reader);
            return doc.Descendants().Where(e => e.Name.LocalName == name)
                .Select(e => e.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (XmlException)
        {
            return LenientElements(xml, name).ToList();
        }
    }

    private static readonly Regex AttrRegex = new("""([A-Za-z_][\w\-]*)\s*=\s*("([^"]*)"|'([^']*)')""", RegexOptions.Compiled);

    private static IEnumerable<Dictionary<string, string>> LenientElements(string xml, string name)
    {
        var elementRegex = new Regex($@"<{Regex.Escape(name)}\b((?:[^>""']|""[^""]*""|'[^']*')*)/?>", RegexOptions.IgnoreCase);
        foreach (Match m in elementRegex.Matches(xml))
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in AttrRegex.Matches(m.Groups[1].Value))
                dict[a.Groups[1].Value] = DecodeEntities(a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value);
            yield return dict;
        }
    }

    private static string DecodeEntities(string s) =>
        Regex.Replace(s, "&(lt|gt|quot|apos|amp|#(\\d+)|#x([0-9a-fA-F]+));", m => m.Groups[1].Value switch
        {
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "amp" => "&",
            _ when m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var d) && IsScalar(d) => char.ConvertFromUtf32(d),
            _ when m.Groups[3].Success && int.TryParse(m.Groups[3].Value, NumberStyles.HexNumber, null, out var x) && IsScalar(x) => char.ConvertFromUtf32(x),
            _ => m.Value,
        });

    private static bool IsScalar(int c) => c is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF);
}
