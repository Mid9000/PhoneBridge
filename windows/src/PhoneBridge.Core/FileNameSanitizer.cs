using System.Text;

namespace PhoneBridge.Core;

/// <summary>Turns an untrusted, client-supplied file name into a safe leaf name for Windows (SPEC §4, §9).</summary>
public static class FileNameSanitizer
{
    const int MaxLength = 200;
    const string Fallback = "file";

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Fallback;
        // Keep only the last path segment, whatever separator the client used.
        var name = raw.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Normalize(NormalizationForm.FormC);

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c < 32 || c == 127 || "<>:\"|?*".Contains(c)) continue;
            // Bidi controls allow "photo‮gpj.exe" style extension spoofing.
            if (c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩')) continue;
            sb.Append(c);
        }
        name = sb.ToString().Trim().TrimEnd('.', ' ');
        name = name.TrimStart('.');   // no hidden/dot-only names (".", "..", ".htaccess")
        if (name.Length == 0) return Fallback;

        var stem = name.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem)) name = "_" + name;

        if (name.Length > MaxLength)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length > 20) ext = "";
            name = name[..(MaxLength - ext.Length)].TrimEnd('.', ' ') + ext;
        }
        return name;
    }

    /// <summary>Returns a path in <paramref name="dir"/> that does not exist yet: "name.ext", then "name (1).ext"...</summary>
    public static string UniquePath(string dir, string safeName)
    {
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        var path = Path.Combine(dir, safeName);
        for (var i = 1; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(dir, $"{stem} ({i}){ext}");
        return path;
    }
}
