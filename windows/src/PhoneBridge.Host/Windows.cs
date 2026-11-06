using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PhoneBridge.Host;

/// <summary>Borderless window with Mica backdrop hosting the Svelte UI in WebView2 (SPEC §10.2).</summary>
class WebWindow : Window
{
    public const string VirtualHost = "app.phonebridge";
    protected readonly WebView2 Web = new() { DefaultBackgroundColor = System.Drawing.Color.Transparent };
    readonly string _fragment;

    public bool Mica { get; private set; }
    public IntPtr Handle { get; private set; }
    public CoreWebView2? Core => Web.CoreWebView2;
    public event Action<WebWindow, CoreWebView2WebMessageReceivedEventArgs>? WebMessage;

    public WebWindow(string fragment)
    {
        _fragment = fragment;
        Background = Brushes.Transparent;
        Icon = AppIcon.CreateImageSource();
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0),
        });
        Content = Web;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(Handle)!.CompositionTarget!.BackgroundColor = Colors.Transparent;
            Mica = Native.ApplyMica(Handle, Native.SystemUsesDarkTheme());
            if (!Mica) Background = new SolidColorBrush(Native.SystemUsesDarkTheme() ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xF5, 0xF5, 0xF7));
        };
        // A maximized chrome-less window overhangs the screen by the resize border: pad it back in.
        StateChanged += (_, _) => Web.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    /// <summary>Loads windows/ui/dist via a virtual host, or the Vite dev server when dist is missing.</summary>
    public async Task<bool> InitializeWebAsync(CoreWebView2Environment env, string? distDir, string devUrl, TimeSpan timeout)
    {
        await Web.EnsureCoreWebView2Async(env);
        var core = Web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsNonClientRegionSupportEnabled = true;   // CSS app-region: drag
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;  // Ctrl+N/P/F belong to the UI, not the browser
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        string url;
        if (distDir is not null)
        {
            core.SetVirtualHostNameToFolderMapping(VirtualHost, distDir, CoreWebView2HostResourceAccessKind.DenyCors);
            url = $"https://{VirtualHost}/index.html";
        }
        else url = devUrl;
        var origin = new Uri(url).GetLeftPart(UriPartial.Authority);

        core.WebMessageReceived += (_, e) =>
        {
            // Trust boundary: only our own page may talk to the host.
            if (e.Source.StartsWith(origin, StringComparison.OrdinalIgnoreCase)) WebMessage?.Invoke(this, e);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(origin, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            Shell.OpenUrl(e.Uri);
        };
        core.NewWindowRequested += (_, e) => { e.Handled = true; Shell.OpenUrl(e.Uri); };

        var done = new TaskCompletionSource<bool>();
        core.NavigationCompleted += (_, e) => done.TrySetResult(e.IsSuccess);
        core.Navigate(url + _fragment);
        var winner = await Task.WhenAny(done.Task, Task.Delay(timeout));
        return winner == done.Task && done.Task.Result;
    }

    public void Post(string json) => Core?.PostWebMessageAsJson(json);
}

sealed class MainWindow : WebWindow
{
    public MainWindow() : base("")
    {
        Title = "PhoneBridge";
        Width = 1100; Height = 720; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}

/// <summary>Tray popup (index.html#quick), placed above the notification area, hidden when it loses focus.</summary>
sealed class QuickWindow : WebWindow
{
    public QuickWindow() : base("#quick")
    {
        Title = "PhoneBridge";
        Width = 320; Height = 440;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Deactivated += (_, _) => Hide();
    }

    public void ShowNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;
        Show();
        Activate();
    }
}

/// <summary>Native window showing the pairing QR code (the secret never transits through the web UI).</summary>
sealed class PairingWindow : Window
{
    public PairingWindow(byte[] qrPng, string host, int port, string fingerprint, DateTimeOffset expiresAt)
    {
        Title = "Appairer un iPhone";
        Icon = AppIcon.CreateImageSource();
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(qrPng);
        image.EndInit();

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, MaxWidth = 320, Margin = new Thickness(0, 12, 0, 0), FontSize = 13,
            Text = $"Scannez ce code avec l'app PhoneBridge sur l'iPhone.\nValable jusqu'à {expiresAt.ToLocalTime():HH:mm}, une seule fois.\n\n" +
                   $"Adresse : {host}:{port}\nEmpreinte : {fingerprint[..16]}…",
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { new Image { Source = image, Width = 320, Height = 320 }, text },
        };

        var timer = new DispatcherTimer { Interval = expiresAt - DateTimeOffset.UtcNow };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }
}
