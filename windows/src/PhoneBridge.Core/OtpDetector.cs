using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoneBridge.Core;

/// <summary>Finds a verification code (4 to 8 digits) next to a keyword (SPEC §8).</summary>
public static partial class OtpDetector
{
    // Matched on accent-stripped lowercase text, so "vérification" == "verification".
    [GeneratedRegex(@"\b(code|codes|verification|verify|2fa|otp|passcode|mot de passe a usage unique|one[- ]time)\b")]
    private static partial Regex Keyword();

    // 4-8 digits, or "123 456" / "123-456"; never glued to other letters/digits (excludes "SOLDES2024", phone numbers).
    [GeneratedRegex(@"(?<![\p{L}\d])(\d{3}[ -]\d{3}|\d{4,8})(?![\p{L}\d]|[.,]\d|\s?[€$%])")]
    private static partial Regex Candidate();

    /// <summary>Returns the code digits only (e.g. "482193"), or null.</summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = StripAccents(text).ToLowerInvariant();
        var keyword = Keyword().Match(plain);
        if (!keyword.Success) return null;

        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (Match m in Candidate().Matches(plain))
        {
            var distance = Math.Abs(m.Index - keyword.Index);
            if (distance < bestDistance) (best, bestDistance) = (m.Value, distance);
        }
        return best?.Replace(" ", "").Replace("-", "");
    }

    /// <summary>"482193" -> "482 193" for display.</summary>
    public static string Format(string code) =>
        code.Length == 6 ? $"{code[..3]} {code[3..]}" : code.Length == 8 ? $"{code[..4]} {code[4..]}" : code;

    static string StripAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}
