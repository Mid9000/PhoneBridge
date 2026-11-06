using System.Text;

namespace PhoneBridge.Spikes;

/// <summary>
/// Console + file log for the spikes. Message content and phone numbers are masked unless --verbose,
/// so the log file can be pasted back safely (SPEC §9).
/// </summary>
public sealed class SpikeLog : IDisposable
{
    private readonly StreamWriter _file;
    private readonly object _lock = new();
    private int _step;
    private readonly List<(string Step, string Result)> _summary = [];

    public bool Verbose { get; }
    public string FilePath { get; }

    public SpikeLog(string tool, bool verbose)
    {
        Verbose = verbose;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge", "spike-logs");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"{tool}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _file = new StreamWriter(FilePath, false, new UTF8Encoding(false)) { AutoFlush = true };
        Write(ConsoleColor.Gray, $"{tool} - log: {FilePath}{(verbose ? "  (VERBOSE: contains message content, do not share)" : "")}");
        Write(ConsoleColor.Gray, $"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")}");
    }

    public void Step(string title)
    {
        _step++;
        Write(ConsoleColor.Cyan, "");
        Write(ConsoleColor.Cyan, $"== [{_step}] {title}");
    }

    public void Info(string text) => Write(ConsoleColor.Gray, "   " + text);
    public void Ok(string text) => Write(ConsoleColor.Green, "   OK   " + text);
    public void Warn(string text) => Write(ConsoleColor.Yellow, "   WARN " + text);
    public void Fail(string text) => Write(ConsoleColor.Red, "   FAIL " + text);
    public void Hint(string text) => Write(ConsoleColor.Magenta, "   -> " + text);

    public void Result(string step, string result) => _summary.Add((step, result));

    /// <summary>For library diagnostics callbacks (they never contain content).</summary>
    public void Trace(string text) => Write(ConsoleColor.DarkGray, "      . " + text);

    /// <summary>User content: shown only with --verbose, otherwise just its length.</summary>
    public string Content(string? value) =>
        value is null ? "(none)" : Verbose ? $"\"{value.Replace("\r", "\\r").Replace("\n", "\\n")}\"" : $"<{value.Length} chars>";

    /// <summary>Phone number / e-mail: "+33******78" unless --verbose.</summary>
    public string Address(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(none)";
        if (Verbose) return value;
        if (value.Contains('@')) return value[..1] + "***@" + value[(value.IndexOf('@') + 1)..].Split('.').Last();
        return value.Length <= 4 ? "****" : value[..Math.Min(3, value.Length - 2)] + new string('*', Math.Max(1, value.Length - 5)) + value[^2..];
    }

    public void PrintSummary()
    {
        Write(ConsoleColor.Cyan, "");
        Write(ConsoleColor.Cyan, "== Summary (paste this whole file back to Claude)");
        foreach (var (step, result) in _summary) Write(ConsoleColor.White, $"   {step,-34} {result}");
        Write(ConsoleColor.Gray, $"Log file: {FilePath}");
    }

    private void Write(ConsoleColor color, string text)
    {
        lock (_lock)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = old;
            _file.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {text}");
        }
    }

    public void Dispose() => _file.Dispose();
}

public static class Args
{
    public static bool Has(string[] args, string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string? Value(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static int Int(string[] args, string name, int fallback) =>
        int.TryParse(Value(args, name), out var v) ? v : fallback;
}
