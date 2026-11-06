using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Web.WebView2.Core;
using PhoneBridge.Bluetooth;
using PhoneBridge.Bluetooth.Platform;
using PhoneBridge.Core;
using PhoneBridge.Server;
using QRCoder;

namespace PhoneBridge.Host;

/// <summary>
/// Glue between the server, Windows (toasts, clipboard, links, tray) and the web UI.
/// UI contract: windows/ui/src/lib/types.ts (UiMessage in, HostMessage out).
/// </summary>
sealed class HostController : IDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly FileLogger _log;
    readonly string _dataDir;
    readonly string _settingsPath;
    readonly AppSettings _settings;
    readonly BridgeServer _server;
    readonly BluetoothService _bt;   // started only when settings.bluetooth is on
    readonly ConcurrentDictionary<long, string> _clientIds = new();   // db message id -> UI optimistic id
    readonly Dictionary<string, int> _unread = new();
    ClipboardService? _clipboard;
    TrayIcon? _tray;
    MainWindow? _main;
    QuickWindow? _quick;
    PairingWindow? _pairing;
    string? _undoClipboard;

    public HostController(string dataDir, FileLogger log, bool customDataDir)
    {
        (_dataDir, _log) = (dataDir, log);
        _settingsPath = Path.Combine(dataDir, "settings.json");
        _settings = AppSettings.Load(_settingsPath);
        // A custom (dev) data dir must not overwrite the real certificate password.
        var secrets = new CredentialStore(customDataDir ? $"PhoneBridge-dev-{Tokens.Hash(dataDir)[..8]}/" : "");
        _server = new BridgeServer(new ServerOptions { DataDir = dataDir, Logger = log, Version = Version, BluetoothState = () => _bt?.State ?? "off" },
            _settings, secrets, BluetoothChannel());
        _bt = new BluetoothService(new WindowsIphoneLinks(log.Write), _server.Db, _settings, log.Write);
    }

    /// <summary>Lazy: the reply queue is built before the Bluetooth service, which needs the server's database.</summary>
    IEnumerable<IReplyChannel> BluetoothChannel()
    {
        if (_bt is not null) yield return _bt;
    }

    public static string Version => typeof(HostController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public BridgeServer Server => _server;
    public MainWindow? Main => _main;
    public bool Quitting { get; private set; }

    public async Task<bool> StartAsync(bool showWindow)
    {
        Wire();
        await _server.StartAsync();
        _log.Write($"Server: https={_server.HttpsPort} http={_server.HttpPort?.ToString() ?? "off"} mdns={_server.MdnsActive} cert={_server.CertSha256[..16]}");
        if (_settings.Bluetooth) _bt.Start();   // background retries: never blocks or fails the startup

        _clipboard = new ClipboardService();
        _clipboard.Copied += OnPcCopied;
        _tray = new TrayIcon(ShowMain, ShowQuick, StartPairing, PushClipboard, SetupFirewall, Quit);

        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_dataDir, "WebView2"));
        var dist = FindUiDist();
        var devUrl = Environment.GetEnvironmentVariable("PHONEBRIDGE_UI_URL") ?? "http://localhost:5173/";
        _log.Write(dist is null ? $"UI: dist not found, using dev server {devUrl}" : "UI: dist found");

        _main = new MainWindow();
        _main.WebMessage += OnWebMessage;
        _main.StateChanged += (_, _) => Post(new { type = "window", maximized = _main.WindowState == WindowState.Maximized });
        _main.Closing += (_, e) =>
        {
            if (Quitting) return;
            e.Cancel = true;   // like a Mac app: closing the window keeps PhoneBridge running in the tray
            _main.Hide();
        };
        if (showWindow) _main.Show();
        else { _main.ShowActivated = false; _main.Show(); }   // smoke: still create the HWND
        var mainOk = await _main.InitializeWebAsync(env, dist, devUrl, TimeSpan.FromSeconds(30));
        _log.Write($"UI main window loaded: {mainOk}, mica={_main.Mica}");

        _quick = new QuickWindow();
        _quick.WebMessage += OnWebMessage;
        _quick.Show(); _quick.Hide();
        var quickOk = await _quick.InitializeWebAsync(env, dist, devUrl, TimeSpan.FromSeconds(30));
        _log.Write($"UI quick window loaded: {quickOk}");
        UpdateTrayText();
        return mainOk && quickOk;
    }

    static string? FindUiDist()
    {
        // Published: <exe>\ui\index.html. Dev: walk up to windows\ui\dist.
        var published = Path.Combine(AppContext.BaseDirectory, "ui");
        if (File.Exists(Path.Combine(published, "index.html"))) return published;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ui", "dist");
            if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
        }
        return null;
    }

    // ------------------------------------------------------------------ server -> Windows

    void Wire()
    {
        _server.FileReceived += t => OnUi(() =>
        {
            Post(new { type = "transfer", transfer = ToUi(t) });
            Toast(b => b.AddText("Fichier reçu de l'iPhone").AddText(t.Name)
                .AddButton(new ToastButton().SetContent("Afficher").AddArgument("action", "reveal").AddArgument("transfer", t.Id.ToString()))
                .AddButton(new ToastButton().SetContent("Ouvrir le dossier").AddArgument("action", "downloads")));
        });
        _server.LinkReceived += (uri, _) => OnUi(() =>
        {
            if (!_settings.ConfirmLinks) { Shell.OpenUrl(uri.AbsoluteUri); return; }
            Toast(b => b.AddText("Lien reçu de l'iPhone").AddText(uri.Host)
                .AddButton(new ToastButton().SetContent("Ouvrir").AddArgument("action", "openLink").AddArgument("url", uri.AbsoluteUri)));
        });
        _server.ClipboardReceived += (payload, sensitive) => OnUi(() =>
        {
            _undoClipboard = _clipboard?.GetText();
            if (payload.Kind == "image" && payload.ImageBase64 is { } b64) _clipboard?.SetImage(Convert.FromBase64String(b64));
            else if (payload.Text is { } text) _clipboard?.SetText(text, sensitive);
            PostClipboard();
            Toast(b =>
            {
                b.AddText("Copié depuis l'iPhone");
                if (_undoClipboard is not null) b.AddButton(new ToastButton().SetContent("Annuler").AddArgument("action", "undoClipboard"));
            }, silent: true);
        });
        _server.MessageReceived += m => OnUi(() => OnMessage(m, unread: true));
        _server.OtpDetected += (code, _) => OnUi(() => OnOtp(code));
        _server.BatteryChanged += _ => OnUi(() => { Post(new { type = "battery", battery = BatteryUi() }); UpdateTrayText(); });
        _server.DevicePaired += d => OnUi(() =>
        {
            _pairing?.Close();
            Post(new { type = "devices", devices = DevicesUi() });
            Post(new { type = "connection", connection = ConnectionUi() });
            Post(new { type = "toast", text = $"{d.Name} est appairé." });
        });
        _server.DeviceSeen += _ => OnUi(() => Post(new { type = "connection", connection = ConnectionUi() }));
        _server.Replies.StatusChanged += m => OnUi(() => Post(new { type = "message", message = ToUi(m) }));
        _server.Outbox.Delivered += item => OnUi(() =>
        {
            if (item.TransferId is long id && _server.Db.GetTransfer(id) is { } t) Post(new { type = "transfer", transfer = ToUi(t) });
        });

        WireBluetooth();

        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            var input = e.UserInput.TryGetValue("reply", out var r) ? r as string : null;
            OnUi(() => OnToastActivated(args, input));
        };
    }

    void WireBluetooth()
    {
        _bt.StateChanged += () => OnUi(() => Post(new { type = "connection", connection = ConnectionUi() }));
        _bt.MessageReceived += (m, unread) => OnUi(() => OnMessage(m, unread));
        _bt.OtpDetected += code => OnUi(() => OnOtp(code));
        _bt.ConversationRead += peer => OnUi(() =>
        {
            lock (_unread) _unread.Remove(peer);
            PostConversations();
        });
        _bt.ContactsLoaded += () => OnUi(PostConversations);
        _bt.NotificationsReset += () => OnUi(() => Post(new { type = "notifications", notifications = NotificationsUi() }));
        _bt.NotificationAdded += n => OnUi(() =>
        {
            Post(new { type = "notification", notification = ToUi(n) });
            if (n.Silent || n.Ringing) return;
            var app = n.AppName ?? n.AppId;
            Toast(b =>
            {
                b.AddText(_settings.HelloLock ? app : n.Title ?? app);
                b.AddText(_settings.HelloLock ? "Nouvelle notification" : n.Body ?? "");
                b.AddAttributionText(app);
            });
        });
        _bt.NotificationRemoved += n => OnUi(() =>
        {
            Post(new { type = "notificationRemoved", id = $"n{n.Id}" });
            if (n.Ringing) Post(new { type = "call", call = CallUi(n, ringing: false) });
        });
        _bt.IncomingCall += n => OnUi(() =>
        {
            Post(new { type = "call", call = CallUi(n, ringing: true) });
            Toast(b => b.SetToastScenario(ToastScenario.IncomingCall)
                .AddText(n.Title ?? "Appel entrant").AddText("Appel entrant sur l'iPhone")
                .AddButton(new ToastButton().SetContent("Refuser").AddArgument("action", "call").AddArgument("id", n.Id.ToString()).AddArgument("answer", "0"))
                .AddButton(new ToastButton().SetContent("Répondre").AddArgument("action", "call").AddArgument("id", n.Id.ToString()).AddArgument("answer", "1")));
        });
    }

    void OnMessage(MessageRecord m, bool unread)
    {
        if (unread) lock (_unread) _unread[m.Peer] = _unread.GetValueOrDefault(m.Peer) + 1;
        Post(new { type = "message", message = ToUi(m) });
        PostConversations();
        if (!unread) return;   // already read on the iPhone
        Toast(b => b.AddArgument("action", "openConversation").AddArgument("peer", m.Peer)
            .AddText(DisplayName(m.Peer)).AddText(_settings.HelloLock ? "Nouveau message" : m.Body)
            .AddInputTextBox("reply", "Répondre…")
            .AddButton(new ToastButton().SetContent("Envoyer").SetTextBoxId("reply")
                .AddArgument("action", "reply").AddArgument("peer", m.Peer).AddArgument("service", m.Service)));
    }

    void OnOtp(string code)
    {
        _clipboard?.SetText(code, sensitive: true);
        Toast(b => b.AddText($"Code {OtpDetector.Format(code)} copié").AddText("Effacé du presse-papiers dans 60 s."), silent: true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => { timer.Stop(); _clipboard?.ClearIfEquals(code); };
        timer.Start();
    }

    string DisplayName(string peer) => _bt.Contacts.NameFor(peer) ?? peer;

    /// <summary>ANCS action from the UI or a toast; tells the user when the iPhone is not reachable.</summary>
    async Task ActOnNotification(long id, bool positive)
    {
        if (!await _bt.PerformActionAsync(id, positive))
            Post(new { type = "toast", text = "Action impossible : l'iPhone n'est pas connecté en Bluetooth.", kind = "error" });
    }

    void OnToastActivated(ToastArguments args, string? reply)
    {
        args.TryGetValue("action", out var action);
        switch (action)
        {
            case "reply" when !string.IsNullOrWhiteSpace(reply) && args.TryGetValue("peer", out var peer):
                _ = SendReply(peer, reply!, args.TryGetValue("service", out var s) ? s : "imessage", null);
                break;
            case "openConversation" when args.TryGetValue("peer", out var p):
                ShowMain();
                Post(new { type = "navigate", section = "messages", conversationId = p });
                break;
            case "reveal" when args.TryGetValue("transfer", out var id) && long.TryParse(id, out var tid):
                if (_server.Db.GetTransfer(tid) is { } t) Shell.Reveal(t.Path);
                break;
            case "call" when args.TryGetValue("id", out var callId) && long.TryParse(callId, out var cid):
                _ = ActOnNotification(cid, args.TryGetValue("answer", out var answer) && answer == "1");
                break;
            case "downloads": Shell.OpenFolder(_settings.DownloadDir); break;
            case "openLink" when args.TryGetValue("url", out var url): Shell.OpenUrl(url); break;
            case "undoClipboard" when _undoClipboard is not null: _clipboard?.SetText(_undoClipboard, false); break;
            default: ShowMain(); break;
        }
    }

    void OnPcCopied(string kind, string? text, string? imageBase64, bool sensitive)
    {
        if (!_settings.ClipboardAuto) return;
        _server.PublishPcClipboard(kind, text, imageBase64, sensitive);
        if (text is not null && !sensitive) PostClipboard();
    }

    void Toast(Action<ToastContentBuilder> build, bool silent = false)
    {
        if (!_settings.Toasts) return;
        try
        {
            var b = new ToastContentBuilder();
            build(b);
            if (silent) b.AddAudio(new ToastAudio { Silent = true });
            b.Show();
        }
        catch (Exception e) { _log.Write($"[Warning] Toast failed: {e.GetType().Name}"); }
    }

    void OnUi(Action action) => _ui.BeginInvoke(() =>
    {
        try { action(); }
        catch (Exception e) { _log.Write($"[Error] UI action failed: {e.GetType().Name} {e.Message}"); }
    });

    // ------------------------------------------------------------------ web UI -> host

    void OnWebMessage(WebWindow source, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement; }
        catch (JsonException) { return; }
        if (msg.ValueKind != JsonValueKind.Object || !msg.TryGetProperty("type", out var typeEl)) return;
        string? Str(string name) => msg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        try
        {
            switch (typeEl.GetString())
            {
                case "ready":
                    Post(new { type = "state", snapshot = Snapshot(source) }, source);
                    _log.Write($"UI ready ({(source is QuickWindow ? "quick" : "main")}): state sent");
                    break;
                case "window": WindowAction(Str("action")); break;
                case "openConversation" when Str("conversationId") is { } conv:
                    lock (_unread) _unread.Remove(conv);
                    _ = _bt.MarkConversationReadAsync(conv);
                    Post(new { type = "messages", conversationId = conv, messages = _server.Db.ListMessages(conv).Select(ToUi) }, source);
                    PostConversations();
                    break;
                case "sendMessage" when Str("body") is { Length: > 0 } body && (Str("conversationId") ?? Str("to"))?.Trim() is { Length: > 0 } to:
                    var service = _server.Db.ListMessages(to, 1).FirstOrDefault()?.Service ?? "imessage";
                    if (Str("conversationId") is null) Post(new { type = "navigate", section = "messages", conversationId = to });
                    _ = SendReply(to, body, service, Str("clientId"));
                    break;
                case "retryMessage" when Str("messageId") is { } mid:
                    var original = FindMessage(mid);
                    if (original is not null) _ = SendReply(original.Peer, original.Body, original.Service, mid);
                    break;
                case "dismissNotification" when Str("id") is { } nid:
                    if (NotificationId(nid) is long n)
                    {
                        // Clear it on the iPhone too, like a Mac; never "decline" a call by dismissing it.
                        if (_bt.Find(n) is { Category: not "call", NegativeAction: not null }) _ = _bt.PerformActionAsync(n, positive: false);
                        _server.Db.DeleteNotification(n);
                    }
                    Post(new { type = "notificationRemoved", id = nid });
                    break;
                case "notificationAction" when NotificationId(Str("id")) is long an:
                    _ = ActOnNotification(an, Str("action") == "positive");   // the iPhone then removes it (ANCS Removed)
                    break;
                case "clearNotifications":
                    _server.Db.ClearNotifications();
                    Post(new { type = "notifications", notifications = Array.Empty<object>() });
                    break;
                case "sendFiles": SendFiles(e); break;
                case "pickFiles": PickFiles(); break;
                case "cancelTransfer" when TransferId(Str("id")) is long cid: CancelTransfer(cid); break;
                case "retryTransfer" when TransferId(Str("id")) is long rid && _server.Db.GetTransfer(rid) is { Direction: "toPhone" } rt:
                    EnqueueFile(rt.Path);
                    break;
                case "revealTransfer" when TransferId(Str("id")) is long vid && _server.Db.GetTransfer(vid) is { } vt: Shell.Reveal(vt.Path); break;
                case "openDownloads": Shell.OpenFolder(_settings.DownloadDir); break;
                case "copyClipboardItem" when ClipboardId(Str("id")) is long kid:
                    if (_server.Db.ListClipboard(500).FirstOrDefault(c => c.Id == kid)?.Text is { } ktext) _clipboard?.SetText(ktext, false);
                    Post(new { type = "toast", text = "Copié dans le presse-papiers du PC" });
                    break;
                case "deleteClipboardItem" when ClipboardId(Str("id")) is long did: _server.Db.DeleteClipboard(did); PostClipboard(); break;
                case "clearClipboard": _server.Db.ClearClipboard(); PostClipboard(); break;
                case "pushClipboard": PushClipboard(); break;
                case "openLink" when Str("url") is { } url: Shell.OpenUrl(url); break;
                case "callAction" when NotificationId(Str("id")) is long callN: _ = ActOnNotification(callN, Str("action") == "answer"); break;
                case "startPairing": StartPairing(); break;
                case "revokeDevice" when Str("id") is { } dev:
                    _server.RevokeDevice(dev);
                    Post(new { type = "devices", devices = DevicesUi() });
                    Post(new { type = "connection", connection = ConnectionUi() });
                    break;
                case "setSettings" when msg.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object:
                    _ = ApplySettings(patch);
                    break;
                case "pickDownloadDir": PickDownloadDir(); break;
                case "retrySection" when Str("section") is { } section: PostSection(section); break;
                case "openMainWindow": _quick?.Hide(); ShowMain(); break;
            }
        }
        catch (Exception ex)
        {
            _log.Write($"[Error] UI message failed: {ex.GetType().Name}");
            Post(new { type = "toast", text = "Une erreur est survenue.", kind = "error" });
        }
    }

    static long? NotificationId(string? id) => long.TryParse(id?.TrimStart('n'), out var v) ? v : null;
    static long? TransferId(string? id) => long.TryParse(id?.TrimStart('t'), out var v) ? v : null;
    static long? ClipboardId(string? id) => long.TryParse(id?.TrimStart('k'), out var v) ? v : null;

    MessageRecord? FindMessage(string uiId)
    {
        var dbId = _clientIds.FirstOrDefault(p => p.Value == uiId).Key;
        if (dbId == 0 && long.TryParse(uiId.TrimStart('m'), out var parsed)) dbId = parsed;
        return dbId == 0 ? null : _server.Db.GetMessage(dbId);
    }

    async Task SendReply(string to, string body, string service, string? clientId)
    {
        if (clientId is not null)
            Post(new { type = "message", message = new { id = clientId, conversationId = to, fromMe = true, body, at = DateTimeOffset.UtcNow, service, status = SendStatus.Sending } });
        try
        {
            var msg = await _server.Replies.SendAsync(to, body, service);
            if (clientId is not null) _clientIds[msg.Id] = clientId;
            Post(new { type = "message", message = ToUi(msg) });
            PostConversations();
        }
        catch (Exception e)
        {
            _log.Write($"[Error] Reply failed: {e.GetType().Name}");
            if (clientId is not null)
                Post(new { type = "message", message = new { id = clientId, conversationId = to, fromMe = true, body, at = DateTimeOffset.UtcNow, service, status = SendStatus.Failed, error = "Envoi impossible." } });
        }
    }

    void WindowAction(string? action)
    {
        if (_main is null) return;
        switch (action)
        {
            case "minimize": _main.WindowState = WindowState.Minimized; break;
            case "maximize": _main.ToggleMaximize(); break;
            case "close": _main.Close(); break;
            case "drag": Native.StartDrag(_main.Handle); break;
        }
    }

    void SendFiles(CoreWebView2WebMessageReceivedEventArgs e)
    {
        var files = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToList() ?? [];
        foreach (var path in files) EnqueueFile(path);
    }

    void PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Envoyer à l'iPhone" };
        if (dialog.ShowDialog(_main) == true) foreach (var path in dialog.FileNames) EnqueueFile(path);
    }

    void EnqueueFile(string path)
    {
        if (!File.Exists(path)) return;   // folders: shortcut, not supported yet (zip them first)
        var item = _server.Outbox.EnqueueFile(path);
        if (_server.Db.GetTransfer(item.TransferId!.Value) is { } t) Post(new { type = "transfer", transfer = ToUi(t) });
    }

    void CancelTransfer(long id)
    {
        var item = _server.Outbox.Pending().FirstOrDefault(o => o.TransferId == id);
        if (item is not null) _server.Db.MarkOutboxDelivered(item.Id, DateTimeOffset.UtcNow);
        _server.Db.UpdateTransferStatus(id, "cancelled");
        if (_server.Db.GetTransfer(id) is { } t) Post(new { type = "transfer", transfer = ToUi(t) });
    }

    void PushClipboard()
    {
        var text = _clipboard?.GetText();
        if (string.IsNullOrEmpty(text))
        {
            Post(new { type = "toast", text = "Le presse-papiers du PC est vide.", kind = "error" });
            return;
        }
        var sensitive = OtpDetector.Detect(text) is not null;
        _server.PublishPcClipboard("text", text, null, sensitive);
        _server.Outbox.EnqueueClipboard(text);
        PostClipboard();
        Post(new { type = "toast", text = "Presse-papiers du PC envoyé à l'iPhone" });
    }

    void StartPairing()
    {
        var qr = _server.CreatePairingPayload();
        var json = JsonSerializer.Serialize(qr, Json);
        using var data = new QRCodeGenerator().CreateQrCode(json, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        _pairing?.Close();
        _pairing = new PairingWindow(png, qr.Host, qr.Port, qr.CertSha256, DateTimeOffset.UtcNow + PairingManager.Lifetime);
        _pairing.Closed += (_, _) => _pairing = null;
        _pairing.Show();
        _pairing.Activate();
    }

    void PickDownloadDir()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier de réception", InitialDirectory = _settings.DownloadDir };
        if (dialog.ShowDialog(_main) != true) return;
        _settings.DownloadDir = dialog.FolderName;
        SaveSettings();
    }

    async Task ApplySettings(JsonElement patch)
    {
        foreach (var p in patch.EnumerateObject())
        {
            var v = p.Value;
            bool B() => v.ValueKind == JsonValueKind.True;
            var isBool = v.ValueKind is JsonValueKind.True or JsonValueKind.False;
            switch (p.Name)
            {
                case "windowStyle" when v.GetString() is "mac" or "windows": _settings.WindowStyle = v.GetString()!; break;
                case "theme" when v.GetString() is "system" or "light" or "dark":
                    _settings.Theme = v.GetString()!;
                    if (_main is not null) Native.SetDarkMode(_main.Handle, _settings.Theme == "dark" || (_settings.Theme == "system" && Native.SystemUsesDarkTheme()));
                    break;
                case "toasts" when isBool: _settings.Toasts = B(); break;
                case "clipboardAuto" when isBool: _settings.ClipboardAuto = B(); break;
                case "otpAutoCopy" when isBool: _settings.OtpAutoCopy = B(); break;
                case "keepHistory" when isBool: _settings.KeepHistory = B(); break;
                case "helloLock" when isBool: _settings.HelloLock = B(); break;
                case "shortcutsHttp" when isBool: await _server.SetShortcutsHttpAsync(B()); break;
                case "bluetooth" when isBool:
                    _settings.Bluetooth = B();
                    if (_settings.Bluetooth) _bt.Start(); else await _bt.StopAsync();
                    break;
                case "downloadDir" when v.GetString() is { } dir && Path.IsPathFullyQualified(dir): _settings.DownloadDir = dir; break;
            }
        }
        SaveSettings();
    }

    void SaveSettings()
    {
        _settings.Save(_settingsPath);
        Post(new { type = "settings", settings = SettingsUi() });
    }

    void SetupFirewall()
    {
        try { Shell.SetupFirewall(); }
        catch (System.ComponentModel.Win32Exception) { /* UAC prompt declined */ }
    }

    // ------------------------------------------------------------------ windows

    public void ShowMain() => _main?.ShowAndActivate();
    void ShowQuick() => _quick?.ShowNearTray();

    public void Quit()
    {
        Quitting = true;
        Application.Current.Shutdown();
    }

    void UpdateTrayText()
    {
        if (_tray is null) return;
        var b = _server.Battery;
        _tray.Text = b is null ? "PhoneBridge" : $"PhoneBridge — iPhone {b.Level} %{(b.Charging ? " (en charge)" : "")}";
    }

    // ------------------------------------------------------------------ host -> UI

    void Post(object message, WebWindow? only = null)
    {
        var json = JsonSerializer.Serialize(message, Json);
        if (only is not null) { only.Post(json); return; }
        _main?.Post(json);
        _quick?.Post(json);
    }

    void PostConversations() => Post(new { type = "conversations", conversations = ConversationsUi() });
    void PostClipboard() => Post(new { type = "clipboard", items = ClipboardUi() });

    void PostSection(string section)
    {
        switch (section)
        {
            case "messages": PostConversations(); break;
            case "notifications": Post(new { type = "notifications", notifications = NotificationsUi() }); break;
            case "files": foreach (var t in _server.Db.ListTransfers()) Post(new { type = "transfer", transfer = ToUi(t) }); break;
            case "clipboard": PostClipboard(); break;
        }
        Post(new { type = "sectionError", section, message = (string?)null });
    }

    object Snapshot(WebWindow window) => new
    {
        host = new { version = Version, mica = window.Mica, maximized = window.WindowState == WindowState.Maximized },
        connection = ConnectionUi(),
        battery = BatteryUi(),
        conversations = ConversationsUi(),
        notifications = NotificationsUi(),
        transfers = _server.Db.ListTransfers().Select(ToUi),
        clipboard = ClipboardUi(),
        calls = _bt.RingingCalls().Select(n => CallUi(n, ringing: true)).ToList(),
        devices = DevicesUi(),
        settings = SettingsUi(),
    };

    object ConnectionUi()
    {
        var devices = _server.Db.ListDevices();
        var last = devices.OrderByDescending(d => d.LastSeenAt ?? d.PairedAt).FirstOrDefault();
        var recent = last?.LastSeenAt is { } seen && DateTimeOffset.UtcNow - seen < TimeSpan.FromMinutes(2);
        return new
        {
            paired = devices.Count > 0,
            phoneName = last?.Name ?? "",
            pcName = _settings.PcName,
            wifi = devices.Count == 0 ? "off" : recent ? "connected" : "disconnected",
            bluetooth = _bt.State,
            bluetoothIssue = _bt.Issue,
            lastSeenAt = last?.LastSeenAt,
        };
    }

    object BatteryUi()
    {
        var b = _server.Battery;
        return new { level = b?.Level, charging = b?.Charging ?? false, updatedAt = b?.UpdatedAt, source = b?.Source };
    }

    IEnumerable<object> ConversationsUi()
    {
        Dictionary<string, int> unread;
        lock (_unread) unread = new(_unread);
        return _server.Db.ListConversationHeads().Select(m => (object)new
        {
            id = m.Peer, name = DisplayName(m.Peer), handle = m.Peer, service = m.Service,
            lastMessage = m.Body, lastAt = m.At, unread = unread.GetValueOrDefault(m.Peer),
        }).ToList();
    }

    object ToUi(MessageRecord m) => new
    {
        id = _clientIds.TryGetValue(m.Id, out var cid) ? cid : m.Id == 0 ? Guid.NewGuid().ToString("N") : $"m{m.Id}",
        conversationId = m.Peer,
        fromMe = m.Direction == "out",
        body = m.Body,
        at = m.At,
        service = m.Service,
        status = m.Direction == "out" ? m.Status : null,
        via = m.Via is "relay" ? "relay" : m.Via is null ? null : "bluetooth",
    };

    static object ToUi(TransferRecord t) => new
    {
        id = $"t{t.Id}", name = t.Name, size = t.Size, transferred = t.Status == "done" ? t.Size : 0L,
        direction = t.Direction, status = t.Status, at = t.At,
    };

    IEnumerable<object> NotificationsUi() => _server.Db.ListNotifications()
        .Select(n => _bt.Find(n.Id) is { } live ? ToUi(live) : new
        {
            id = $"n{n.Id}", appId = n.AppId, appName = n.AppName ?? n.AppId, category = n.Category ?? "other",
            title = n.Title ?? "", body = n.Body ?? "", at = n.At,
        }).ToList();

    static object ToUi(PhoneNotice n) => new
    {
        id = $"n{n.Id}", appId = n.AppId, appName = n.AppName ?? n.AppId, category = n.Category, title = n.Title ?? "",
        subtitle = n.Subtitle, body = n.Body ?? "", at = n.At, positiveAction = n.PositiveAction, negativeAction = n.NegativeAction,
    };

    static object CallUi(PhoneNotice n, bool ringing) => new
    {
        id = $"n{n.Id}", name = n.Title ?? "Inconnu", handle = n.Title ?? "", kind = "incoming", at = n.At, ringing,
    };

    IEnumerable<object> ClipboardUi() => _server.Db.ListClipboard().Select(c => (object)new
    {
        id = $"k{c.Id}", kind = c.Kind, text = c.Text, origin = c.Origin, at = c.At,
    }).ToList();

    IEnumerable<object> DevicesUi()
    {
        var devices = _server.Db.ListDevices();
        var current = devices.OrderByDescending(d => d.LastSeenAt ?? d.PairedAt).FirstOrDefault()?.Id;
        return devices.Select(d => (object)new { id = d.Id, name = d.Name, pairedAt = d.PairedAt, lastSeenAt = d.LastSeenAt, current = d.Id == current }).ToList();
    }

    object SettingsUi() => new
    {
        windowStyle = _settings.WindowStyle, theme = _settings.Theme, toasts = _settings.Toasts,
        clipboardAuto = _settings.ClipboardAuto, otpAutoCopy = _settings.OtpAutoCopy, keepHistory = _settings.KeepHistory,
        helloLock = _settings.HelloLock, shortcutsHttp = _settings.ShortcutsHttp, downloadDir = _settings.DownloadDir,
        bluetooth = _settings.Bluetooth,
    };

    /// <summary>Dev/smoke only: writes the QR payload so scripts can pair without a camera.</summary>
    public string WriteDevPairingFile()
    {
        var path = Path.Combine(_dataDir, "dev-pairing.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_server.CreatePairingPayload("127.0.0.1"), Json));
        return path;
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _clipboard?.Dispose();
        // Off the UI thread: the async stops must not wait on the (blocked) WPF dispatcher.
        Task.Run(() => _bt.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(10));
        Task.Run(() => _server.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(10));
    }
}
