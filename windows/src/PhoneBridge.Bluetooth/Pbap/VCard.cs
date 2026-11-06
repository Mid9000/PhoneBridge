using System.Globalization;
using System.Text;

namespace PhoneBridge.Bluetooth.Pbap;

public sealed record VCardPhone(string Number, IReadOnlyList<string> Types);

/// <summary>vCard 2.1 / 3.0 subset used by PBAP and bMessage: names, phones, e-mails, photo, call-history stamp.</summary>
public sealed class VCard
{
    public string? Version { get; set; }
    public string? FormattedName { get; set; }
    /// <summary>N components: family;given;additional;prefix;suffix.</summary>
    public IReadOnlyList<string> NameParts { get; set; } = [];
    public List<VCardPhone> Phones { get; } = [];
    public List<string> Emails { get; } = [];
    public string? Organization { get; set; }
    public byte[]? Photo { get; set; }
    /// <summary>X-IRMC-CALL-DATETIME type: MISSED, RECEIVED or DIALED (call history only).</summary>
    public string? CallType { get; set; }
    public DateTimeOffset? CallDateTime { get; set; }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FormattedName)) return FormattedName!;
            var given = NameParts.ElementAtOrDefault(1);
            var family = NameParts.ElementAtOrDefault(0);
            var n = string.Join(" ", new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (n.Length > 0) return n;
            return Organization ?? Phones.FirstOrDefault()?.Number ?? Emails.FirstOrDefault() ?? "";
        }
    }

    public static IReadOnlyList<VCard> ParseMany(string text)
    {
        var cards = new List<VCard>();
        VCard? current = null;
        foreach (var line in Unfold(text))
        {
            var (name, parameters, value) = SplitProperty(line);
            if (name == "BEGIN" && value.Equals("VCARD", StringComparison.OrdinalIgnoreCase)) { current = new VCard(); continue; }
            if (current is null) continue;
            if (name == "END" && value.Equals("VCARD", StringComparison.OrdinalIgnoreCase)) { cards.Add(current); current = null; continue; }
            current.Apply(name, parameters, value);
        }
        return cards;
    }

    private void Apply(string name, Dictionary<string, List<string>> parameters, string rawValue)
    {
        string Text() => Decode(parameters, rawValue);
        switch (name)
        {
            case "VERSION": Version = rawValue.Trim(); break;
            case "FN": FormattedName = Unescape(Text()); break;
            case "N": NameParts = SplitStructured(Text()); break;
            case "ORG": Organization = SplitStructured(Text()).FirstOrDefault(); break;
            case "TEL":
                var number = Unescape(Text()).Trim();
                if (number.Length > 0) Phones.Add(new VCardPhone(number, Types(parameters)));
                break;
            case "EMAIL":
                var email = Unescape(Text()).Trim();
                if (email.Length > 0) Emails.Add(email);
                break;
            case "PHOTO":
                if (IsBase64(parameters))
                {
                    try { Photo = Convert.FromBase64String(new string(rawValue.Where(c => !char.IsWhiteSpace(c)).ToArray())); }
                    catch (FormatException) { Photo = null; }
                }
                break;
            case "X-IRMC-CALL-DATETIME":
                CallType = Types(parameters).FirstOrDefault()?.ToUpperInvariant();
                CallDateTime = ParseCallTime(rawValue.Trim());
                break;
        }
    }

    private static DateTimeOffset? ParseCallTime(string v)
    {
        string[] formats = ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmmss'Z'"];
        var style = v.EndsWith('Z') ? DateTimeStyles.AssumeUniversal : DateTimeStyles.AssumeLocal;
        return DateTimeOffset.TryParseExact(v, formats, CultureInfo.InvariantCulture, style, out var t) ? t : null;
    }

    /// <summary>TYPE values from both "TYPE=CELL,VOICE" (3.0) and bare ";CELL" (2.1) forms.</summary>
    private static IReadOnlyList<string> Types(Dictionary<string, List<string>> p) =>
        p.TryGetValue("TYPE", out var t) ? t : [];

    private static bool IsBase64(Dictionary<string, List<string>> p) =>
        p.TryGetValue("ENCODING", out var e) && e.Any(v => v.Equals("b", StringComparison.OrdinalIgnoreCase) || v.Equals("BASE64", StringComparison.OrdinalIgnoreCase));

    private static string Decode(Dictionary<string, List<string>> p, string value)
    {
        bool qp = p.TryGetValue("ENCODING", out var e) && e.Any(v => v.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase));
        if (!qp) return value;
        var bytes = new List<byte>();
        int plainStart = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length &&
                byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, null, out var b))
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[plainStart..i]));
                bytes.Add(b);
                i += 2;
                plainStart = i + 1;
            }
        }
        bytes.AddRange(Encoding.UTF8.GetBytes(value[plainStart..]));
        // shortcut: assumes CHARSET=UTF-8 for quoted-printable (what iOS sends); other charsets need Encoding.GetEncoding.
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static IReadOnlyList<string> SplitStructured(string v)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length) { sb.Append(v[i]).Append(v[i + 1]); i++; }
            else if (v[i] == ';') { parts.Add(Unescape(sb.ToString())); sb.Clear(); }
            else sb.Append(v[i]);
        }
        parts.Add(Unescape(sb.ToString()));
        return parts;
    }

    private static string Unescape(string v)
    {
        if (!v.Contains('\\')) return v;
        var sb = new StringBuilder(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length)
            {
                char n = v[++i];
                sb.Append(n is 'n' or 'N' ? '\n' : n);
            }
            else sb.Append(v[i]);
        }
        return sb.ToString();
    }

    /// <summary>Splits "NAME;P1=a,b;P2:value" (group prefixes like "item1." are dropped).</summary>
    private static (string Name, Dictionary<string, List<string>> Params, string Value) SplitProperty(string line)
    {
        int colon = IndexOfUnquoted(line, ':');
        string head = colon < 0 ? line : line[..colon];
        string value = colon < 0 ? "" : line[(colon + 1)..];
        var pieces = head.Split(';');
        string name = pieces[0].Trim();
        int dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        var parameters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in pieces.Skip(1))
        {
            int eq = piece.IndexOf('=');
            string key = eq < 0 ? "TYPE" : piece[..eq].Trim();
            string vals = eq < 0 ? piece : piece[(eq + 1)..];
            if (!parameters.TryGetValue(key, out var list)) parameters[key] = list = [];
            list.AddRange(vals.Split(',').Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0));
        }
        // vCard 2.1 bare "QUOTED-PRINTABLE" / "BASE64" land in TYPE; move them where Decode looks.
        if (parameters.TryGetValue("TYPE", out var types))
        {
            foreach (var enc in types.Where(t => t.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) || t.Equals("BASE64", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                types.Remove(enc);
                if (!parameters.TryGetValue("ENCODING", out var e)) parameters["ENCODING"] = e = [];
                e.Add(enc);
            }
        }
        return (name.ToUpperInvariant(), parameters, value);
    }

    private static int IndexOfUnquoted(string s, char c)
    {
        bool quoted = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') quoted = !quoted;
            else if (s[i] == c && !quoted) return i;
        }
        return -1;
    }

    /// <summary>RFC 2425 folding (CRLF + space/tab) and vCard 2.1 quoted-printable soft breaks ("=" at line end).</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        bool qpSoft = false;
        foreach (var line in lines)
        {
            if (sb.Length > 0 && (qpSoft || (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))))
            {
                sb.Append(qpSoft ? line : line[1..]);
            }
            else
            {
                if (sb.Length > 0) yield return sb.ToString();
                sb.Clear().Append(line);
            }
            qpSoft = sb.Length > 0 && sb[^1] == '=' && sb.ToString().Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
            if (qpSoft) sb.Length--;
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
