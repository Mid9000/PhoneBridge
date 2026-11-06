using System.Text;
using System.Text.RegularExpressions;
using PhoneBridge.Bluetooth.Pbap;

namespace PhoneBridge.Bluetooth.Map;

/// <summary>bMessage (MAP 1.4 §3.1.3), SMS/iMessage subset: one originator, recipients in BENV, one text body.</summary>
public sealed class BMessage
{
    public string Status { get; init; } = "UNREAD";
    public string Type { get; init; } = "SMS_GSM";
    public string Folder { get; init; } = "";
    public VCard? Originator { get; init; }
    public IReadOnlyList<VCard> Recipients { get; init; } = [];
    public string Charset { get; init; } = "UTF-8";
    public string Body { get; init; } = "";

    /// <summary>Builds the PushMessage bMessage. Shape follows what iOS accepted for BlueFerry/iphonebridge (see docs/map-notes.md).</summary>
    public static byte[] BuildForPush(string recipient, string text)
    {
        var r = ValidateRecipient(recipient);
        var body = StuffBody(text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"));
        // LENGTH counts "BEGIN:MSG\r\n" + content + "\r\nEND:MSG\r\n" in bytes.
        string msg = "BEGIN:MSG\r\n" + body + "\r\nEND:MSG\r\n";
        int length = Encoding.UTF8.GetByteCount(msg);
        bool isEmail = r.Contains('@');

        var sb = new StringBuilder();
        void L(string line) => sb.Append(line).Append("\r\n");
        L("BEGIN:BMSG");
        L("VERSION:1.0");
        L("STATUS:READ");
        L("TYPE:SMS_GSM"); // iOS labels SMS and iMessage alike; it picks the transport itself.
        L("FOLDER:telecom/msg/outbox");
        L("BEGIN:VCARD");
        L("VERSION:2.1");
        L("N:");
        L("TEL:");
        L("END:VCARD");
        L("BEGIN:BENV");
        L("BEGIN:VCARD");
        L("VERSION:2.1");
        L("N:");
        L(isEmail ? "EMAIL:" + r : "TEL:" + r);
        L("END:VCARD");
        L("BEGIN:BBODY");
        L("CHARSET:UTF-8");
        L("LENGTH:" + length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(msg);
        L("END:BBODY");
        L("END:BENV");
        L("END:BMSG");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static readonly Regex PhoneRegex = new(@"^\+?[0-9 ()\-\.]{2,30}$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^\s@;:,<>""\\]+@[^\s@;:,<>""\\]+\.[^\s@;:,<>""\\]+$", RegexOptions.Compiled);

    /// <summary>Phone number (kept with +, digits only) or e-mail (Apple ID). Throws on anything that could break the vCard.</summary>
    public static string ValidateRecipient(string recipient)
    {
        var r = recipient.Trim();
        if (r.Length > 254 || r.Any(char.IsControl)) throw new ArgumentException("Invalid recipient", nameof(recipient));
        if (EmailRegex.IsMatch(r)) return r;
        if (PhoneRegex.IsMatch(r))
        {
            var digits = new string(r.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length >= 2) return (r.StartsWith('+') ? "+" : "") + digits;
        }
        throw new ArgumentException("Recipient must be a phone number or an e-mail address", nameof(recipient));
    }

    // A body line that looks like the end marker would truncate the message; prefix it with a space (and undo on parse).
    private static string StuffBody(string body) =>
        string.Join("\r\n", body.Split("\r\n").Select(l => IsMarker(l.TrimStart(' ')) ? " " + l : l));

    private static string UnstuffLine(string l) => l.StartsWith(' ') && IsMarker(l.TrimStart(' ')) ? l[1..] : l;

    private static bool IsMarker(string l) =>
        l.StartsWith("END:MSG", StringComparison.OrdinalIgnoreCase) || l.StartsWith("BEGIN:MSG", StringComparison.OrdinalIgnoreCase);

    public static BMessage Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string status = "", type = "", folder = "", charset = "UTF-8";
        VCard? originator = null;
        var recipients = new List<VCard>();
        var body = new StringBuilder();
        int depthEnv = 0;
        bool inMsg = false, bodyDone = false;
        var vcardLines = (List<string>?)null;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (inMsg)
            {
                if (line.Equals("END:MSG", StringComparison.OrdinalIgnoreCase))
                {
                    inMsg = false;
                    bodyDone = true;
                    continue;
                }
                if (body.Length > 0) body.Append('\n');
                body.Append(UnstuffLine(line));
                continue;
            }
            if (vcardLines is not null)
            {
                if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    var card = VCard.ParseMany("BEGIN:VCARD\n" + string.Join("\n", vcardLines) + "\nEND:VCARD").FirstOrDefault() ?? new VCard();
                    if (depthEnv == 0 && originator is null) originator = card; else recipients.Add(card);
                    vcardLines = null;
                }
                else vcardLines.Add(line);
                continue;
            }

            var (key, value) = Split(line);
            switch (key)
            {
                case "STATUS" when depthEnv == 0: status = value; break;
                case "TYPE" when depthEnv == 0: type = value; break;
                case "FOLDER" when depthEnv == 0: folder = value; break;
                case "CHARSET": charset = value; break;
                case "BEGIN" when value.Equals("VCARD", StringComparison.OrdinalIgnoreCase): vcardLines = []; break;
                case "BEGIN" when value.Equals("BENV", StringComparison.OrdinalIgnoreCase): depthEnv++; break;
                case "END" when value.Equals("BENV", StringComparison.OrdinalIgnoreCase): depthEnv = Math.Max(0, depthEnv - 1); break;
                case "BEGIN" when value.Equals("MSG", StringComparison.OrdinalIgnoreCase) && !bodyDone: inMsg = true; break;
            }
        }

        return new BMessage
        {
            Status = status,
            Type = type,
            Folder = folder,
            Charset = charset,
            Originator = originator,
            Recipients = recipients,
            Body = body.ToString(),
        };
    }

    private static (string Key, string Value) Split(string line)
    {
        int i = line.IndexOf(':');
        return i < 0 ? (line.Trim().ToUpperInvariant(), "") : (line[..i].Trim().ToUpperInvariant(), line[(i + 1)..].Trim());
    }
}
