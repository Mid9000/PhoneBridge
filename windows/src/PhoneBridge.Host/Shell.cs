using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PhoneBridge.Host;

/// <summary>Opening links, folders and helper scripts.</summary>
static class Shell
{
    public static bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return true;
    }

    public static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { dir } });
    }

    public static void Reveal(string path)
    {
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else OpenFolder(Path.GetDirectoryName(path)!);
    }

    /// <summary>Runs scripts/firewall.ps1 elevated (UAC prompt). Rules are limited to the Private profile.</summary>
    public static void SetupFirewall()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "scripts", "firewall.ps1");
        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Program", Environment.ProcessPath! },
            UseShellExecute = true,
            Verb = "runas",
        });
    }
}

/// <summary>App icon drawn at runtime (no binary asset, nothing from Apple): blue rounded square with a phone outline.</summary>
static class AppIcon
{
    public static Drawing.Icon Create(int size = 32)
    {
        using var bmp = new Drawing.Bitmap(size, size);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = RoundedRect(new Drawing.RectangleF(1, 1, size - 2, size - 2), size * 0.24f);
            using var blue = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x00, 0x7A, 0xFF));
            g.FillPath(blue, bg);
            using var phone = RoundedRect(new Drawing.RectangleF(size * 0.34f, size * 0.2f, size * 0.32f, size * 0.6f), size * 0.07f);
            using var pen = new Drawing.Pen(Drawing.Color.White, Math.Max(1.5f, size / 14f));
            g.DrawPath(pen, phone);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    public static BitmapSource CreateImageSource()
    {
        using var icon = Create(64);
        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.RectangleF r, float radius)
    {
        var p = new Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Tray icon: left click = quick menu, double click = main window, right click = native menu.</summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action openMain, Action showQuick, Action pair, Action pushClipboard, Action firewall, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir PhoneBridge", null, (_, _) => openMain());
        menu.Items.Add("Envoyer le presse-papiers à l'iPhone", null, (_, _) => pushClipboard());
        menu.Items.Add("Appairer un iPhone…", null, (_, _) => pair());
        menu.Items.Add("Autoriser dans le pare-feu (réseau privé)…", null, (_, _) => firewall());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => quit());
        _icon = new Forms.NotifyIcon { Icon = AppIcon.Create(), Text = "PhoneBridge", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) showQuick(); };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) openMain(); };
    }

    public string Text { set => _icon.Text = value.Length > 63 ? value[..63] : value; }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>Plain file log in %LOCALAPPDATA%\PhoneBridge\logs. Callers never pass message/clipboard content.</summary>
sealed class FileLogger(string path) : ILogger
{
    readonly object _lock = new();

    public void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch (IOException) { }
        }
        Console.WriteLine(line);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel)) Write($"[{logLevel}] {formatter(state, exception)}");
    }
}
