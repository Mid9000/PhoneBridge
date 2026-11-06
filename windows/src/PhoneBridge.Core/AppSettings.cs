using System.Text.Json;

namespace PhoneBridge.Core;

/// <summary>User settings, persisted as JSON. Field names match the UI contract (windows/ui/src/lib/types.ts).</summary>
public sealed class AppSettings
{
    public string WindowStyle { get; set; } = "mac";          // "mac" | "windows"
    public string Theme { get; set; } = "system";             // "system" | "light" | "dark"
    public bool Toasts { get; set; } = true;
    public bool ClipboardAuto { get; set; } = true;           // publish PC copies to GET /clipboard
    public bool OtpAutoCopy { get; set; } = true;
    public bool KeepHistory { get; set; } = true;
    public bool HelloLock { get; set; }
    public bool ShortcutsHttp { get; set; } = true;           // SPEC §4: on by default in phase 1
    public string DownloadDir { get; set; } = DefaultDownloadDir();
    public bool ConfirmLinks { get; set; }
    public long MaxFileSizeMb { get; set; } = 4096;
    public string PcName { get; set; } = Environment.MachineName;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString();

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultDownloadDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PhoneBridge");

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new();
        }
        catch (JsonException) { /* corrupted file: fall back to defaults, it is rewritten on next save */ }
        var fresh = new AppSettings();
        fresh.Save(path);   // persists the generated DeviceId
        return fresh;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
