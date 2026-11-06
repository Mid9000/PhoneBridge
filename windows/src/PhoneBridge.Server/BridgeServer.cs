using System.Buffers;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhoneBridge.Core;
using PhoneBridge.Core.Protocol;

namespace PhoneBridge.Server;

public sealed class ServerOptions
{
    public required string DataDir { get; init; }
    public int HttpsPort { get; init; } = 47821;   // 0 = pick a free port (tests)
    public int HttpPort { get; init; } = 47822;
    public bool AdvertiseMdns { get; init; } = true;
    public string Version { get; init; } = "0.1.0";
    public Func<string> BluetoothState { get; init; } = () => "off";
    public ILogger Logger { get; init; } = NullLogger.Instance;
}

/// <summary>
/// Wi-Fi channel A (SPEC §4, docs/protocol.md): HTTPS on 47821 with every route, plain HTTP on 47822 with only the
/// [shortcuts] routes. Both refuse non-private IPs and require a device token. Events let the Host react (toasts, clipboard, links).
/// </summary>
public sealed class BridgeServer : IAsyncDisposable
{
    public const string ApiPrefix = "/v1";

    readonly ServerOptions _options;
    readonly ILogger _log;
    readonly X509Certificate2 _cert;
    readonly EventHub _hub = new();
    readonly MdnsAdvertiser _mdns = new();
    readonly RateLimiter _pairLimiter;
    readonly RateLimiter _authLimiter;
    readonly Dictionary<string, (DeviceRecord Device, DateTimeOffset Touched)> _tokenCache = new();
    readonly SemaphoreSlim _httpToggle = new(1, 1);
    WebApplication? _https, _http;

    public BridgeServer(ServerOptions options, AppSettings settings, ISecretStore secrets,
        IEnumerable<IReplyChannel>? replyChannels = null, TimeProvider? time = null)
    {
        _options = options;
        _log = options.Logger;
        Time = time ?? TimeProvider.System;
        Settings = settings;
        Db = new Database(Path.Combine(options.DataDir, "phonebridge.db"));
        Pairing = new PairingManager(Time);
        Outbox = new OutboxQueue(Db, Time);
        Replies = new ReplyQueue(Db, Outbox, replyChannels, Time);
        _pairLimiter = new RateLimiter(5, TimeSpan.FromMinutes(1), Time);
        _authLimiter = new RateLimiter(10, TimeSpan.FromMinutes(1), Time);
        _cert = CertificateManager.LoadOrCreate(Path.Combine(options.DataDir, "server.pfx"), secrets);
        CertSha256 = CertificateManager.Sha256(_cert);
        Outbox.Changed += count => _hub.Broadcast(new OutboxEvent("outbox", count));
        _mdns.Completed += status => _log.LogInformation("mDNS registration completed: status {Status}", status);
    }

    public TimeProvider Time { get; }
    public AppSettings Settings { get; }
    public Database Db { get; }
    public PairingManager Pairing { get; }
    public OutboxQueue Outbox { get; }
    public ReplyQueue Replies { get; }
    public string CertSha256 { get; }
    public int HttpsPort { get; private set; }
    public int? HttpPort { get; private set; }
    public bool MdnsActive { get; private set; }
    public BatteryState? Battery { get; private set; }
    public ClipboardPayload? PcClipboard { get; private set; }

    public event Action<TransferRecord>? FileReceived;
    public event Action<Uri, string?>? LinkReceived;
    /// <summary>bool = sensitive (OTP-like): set it on the PC clipboard with history/cloud exclusion.</summary>
    public event Action<ClipboardPayload, bool>? ClipboardReceived;
    public event Action<MessageRecord>? MessageReceived;
    /// <summary>Code digits, then the source ("message").</summary>
    public event Action<string, string>? OtpDetected;
    public event Action<BatteryState>? BatteryChanged;
    public event Action<DeviceRecord>? DevicePaired;
    public event Action<DeviceRecord>? DeviceSeen;

    public async Task StartAsync()
    {
        _https = Build(https: true, _options.HttpsPort);
        await _https.StartAsync();
        HttpsPort = BoundPort(_https);
        if (Settings.ShortcutsHttp) await SetShortcutsHttpAsync(true);
        if (_options.AdvertiseMdns)
        {
            MdnsActive = _mdns.Start(Settings.PcName.Replace('.', ' '), (ushort)HttpsPort, NetworkRules.PrimaryLanAddress(),
                new Dictionary<string, string> { ["v"] = "1", ["id"] = Settings.DeviceId, ["name"] = Settings.PcName });
            _log.LogInformation("mDNS advertise: {Active} (status {Status})", MdnsActive, _mdns.LastStatus);
        }
        _log.LogInformation("Server started: https={Https} http={Http}", HttpsPort, HttpPort);
    }

    /// <summary>Opens or closes the plain-HTTP listener for iOS Shortcuts (SPEC §4).</summary>
    public async Task SetShortcutsHttpAsync(bool enabled)
    {
        await _httpToggle.WaitAsync();
        try
        {
            Settings.ShortcutsHttp = enabled;
            if (enabled && _http is null)
            {
                var app = Build(https: false, _options.HttpPort);
                await app.StartAsync();
                (_http, HttpPort) = (app, BoundPort(app));
            }
            else if (!enabled && _http is not null)
            {
                await _http.StopAsync();
                await _http.DisposeAsync();
                (_http, HttpPort) = (null, null);
            }
        }
        finally { _httpToggle.Release(); }
    }

    public PairingQrPayload CreatePairingPayload(string? host = null)
    {
        var offer = Pairing.CreateOffer();
        host ??= NetworkRules.PrimaryLanAddress()?.ToString() ?? "127.0.0.1";
        return new PairingQrPayload(1, host, HttpsPort, CertSha256, offer.Secret, Settings.DeviceId, Settings.PcName);
    }

    public bool RevokeDevice(string id)
    {
        lock (_tokenCache) _tokenCache.Clear();
        return Db.RevokeDevice(id);
    }

    /// <summary>Latest PC clipboard, served by GET /clipboard. Sensitive content is served but never stored in history.</summary>
    public void PublishPcClipboard(string kind, string? text, string? imageBase64, bool sensitive)
    {
        PcClipboard = new ClipboardPayload(kind, text, imageBase64, Time.GetUtcNow());
        if (Settings.KeepHistory && !sensitive && text is not null)
            Db.AddClipboard(new ClipboardRecord(0, kind, text, "pc", Time.GetUtcNow()));
    }

    public async ValueTask DisposeAsync()
    {
        _mdns.Dispose();
        foreach (var app in new[] { _http, _https })
        {
            if (app is null) continue;
            await app.StopAsync();
            await app.DisposeAsync();
        }
        (_http, _https) = (null, null);
        _cert.Dispose();
    }

    // ---------------------------------------------------------------- pipeline

    WebApplication Build(bool https, int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(BridgeServer).Assembly.GetName().Name,
        });
        // Hosting logs would print request URLs (WebSocket ?token=): keep framework logs at Warning, never content.
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.ListenAnyIP(port, listen =>
            {
                if (https)
                    listen.UseHttps(new HttpsConnectionAdapterOptions
                    {
                        ServerCertificate = _cert,
                        SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    });
            });
        });
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.Use(Gate);
        MapRoutes(app, shortcutsOnly: !https);
        return app;
    }

    static int BoundPort(WebApplication app)
    {
        var url = app.Urls.First();
        return int.Parse(url[(url.LastIndexOf(':') + 1)..].TrimEnd('/'));
    }

    static string IpKey(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return (ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip)?.ToString() ?? "?";
    }

    internal static IResult Error(int status, string code, string message) =>
        Results.Json(new ErrorResponse(code, message), statusCode: status);

    static Task Write(HttpContext ctx, int status, string code, string message) =>
        Error(status, code, message).ExecuteAsync(ctx);

    /// <summary>Private-network check, unknown route, auth-failure rate limit, Bearer token. Public for tests.</summary>
    public async Task Gate(HttpContext ctx, RequestDelegate next)
    {
        if (!NetworkRules.IsPrivate(ctx.Connection.RemoteIpAddress))
        {
            await Write(ctx, 403, "forbidden_network", "Only local network clients are allowed.");
            return;
        }
        if (ctx.GetEndpoint() is null)
        {
            await Write(ctx, 404, "not_found", "Unknown route.");
            return;
        }
        var path = ctx.Request.Path.Value ?? "";
        if (path == ApiPrefix + "/pair") { await next(ctx); return; }

        var ip = IpKey(ctx);
        if (_authLimiter.IsLimited(ip))
        {
            await Write(ctx, 429, "rate_limited", "Too many failed attempts, retry in a minute.");
            return;
        }

        string? token = null;
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = auth[7..].Trim();
        else if (path == ApiPrefix + "/events") token = ctx.Request.Query["token"].ToString();

        var device = string.IsNullOrEmpty(token) ? null : Authenticate(token);
        if (device is null)
        {
            if (string.IsNullOrEmpty(token) && path == ApiPrefix + "/status" && HttpMethods.IsGet(ctx.Request.Method))
            {
                await next(ctx);   // anonymous status: name + version only
                return;
            }
            _authLimiter.Hit(ip);
            await Write(ctx, 401, "unauthorized", "Missing or invalid token.");
            return;
        }
        ctx.Items["device"] = device;
        await next(ctx);
    }

    DeviceRecord? Authenticate(string token)
    {
        var hash = Tokens.Hash(token);
        var now = Time.GetUtcNow();
        lock (_tokenCache)
        {
            if (_tokenCache.TryGetValue(hash, out var hit) && now - hit.Touched < TimeSpan.FromSeconds(30)) return hit.Device;
        }
        var device = Db.FindDeviceByTokenHash(hash);
        if (device is null) return null;
        Db.TouchDevice(device.Id, now);
        device = device with { LastSeenAt = now };
        lock (_tokenCache) _tokenCache[hash] = (device, now);
        Raise(DeviceSeen, device);
        return device;
    }

    void Raise<T>(Action<T>? handler, T arg)
    {
        try { handler?.Invoke(arg); }
        catch (Exception e) { _log.LogError("Event handler failed: {Type}", e.GetType().Name); }
    }

    // ---------------------------------------------------------------- routes

    void MapRoutes(WebApplication app, bool shortcutsOnly)
    {
        var v1 = app.MapGroup(ApiPrefix);

        // [shortcuts] routes, also on HTTP 47822
        v1.MapGet("/status", Status);
        v1.MapPost("/files", (Delegate)UploadFile);
        v1.MapPost("/links", PostLink);
        v1.MapPost("/clipboard", PostClipboard);
        v1.MapGet("/clipboard", () => PcClipboard is null ? Results.NoContent() : Results.Json(PcClipboard));
        v1.MapPost("/messages", PostMessage);
        v1.MapPost("/battery", PostBattery);
        v1.MapGet("/outbox", () => Results.Json(new OutboxListResponse(Outbox.Pending())));
        v1.MapGet("/outbox/{id}", GetOutboxItem);
        v1.MapDelete("/outbox/{id}", (string id) => Outbox.Acknowledge(id) ? Results.NoContent() : Error(404, "not_found", "Unknown outbox item."));
        if (shortcutsOnly) return;

        v1.MapPost("/pair", Pair);
        v1.Map("/events", Events);
    }

    IResult Status(HttpContext ctx)
    {
        if (ctx.Items["device"] is null) return Results.Json(new StatusResponse(Settings.PcName, _options.Version));
        return Results.Json(new StatusResponse(Settings.PcName, _options.Version,
            new StatusFeatures(HttpPort is not null, _options.BluetoothState()), Time.GetUtcNow(), Battery));
    }

    IResult Pair(HttpContext ctx, PairRequest req)
    {
        var ip = IpKey(ctx);
        if (_pairLimiter.IsLimited(ip)) return Error(429, "rate_limited", "Too many pairing attempts, retry in a minute.");
        switch (Pairing.TryRedeem(req.PairingSecret))
        {
            case PairOutcome.Invalid:
                _pairLimiter.Hit(ip);
                return Error(401, "invalid_secret", "Pairing secret is invalid.");
            case PairOutcome.Expired:
                _pairLimiter.Hit(ip);
                return Error(410, "secret_expired", "Pairing secret expired or already used. Show a new QR code on the PC.");
        }
        var token = Tokens.NewToken();
        var id = Guid.TryParse(req.DeviceId, out var g) ? g.ToString() : Guid.NewGuid().ToString();
        var name = string.IsNullOrWhiteSpace(req.DeviceName) ? "iPhone" : req.DeviceName.Trim()[..Math.Min(100, req.DeviceName.Trim().Length)];
        var device = new DeviceRecord(id, name, Time.GetUtcNow(), null);
        Db.AddDevice(device, Tokens.Hash(token));
        lock (_tokenCache) _tokenCache.Clear();   // a re-paired device loses its old token
        _log.LogInformation("Device paired");
        Raise(DevicePaired, device);
        return Results.Json(new PairResponse(token, Settings.PcName, HttpPort ?? _options.HttpPort));
    }

    async Task<IResult> UploadFile(HttpContext ctx)
    {
        var req = ctx.Request;
        var rawName = req.Headers["X-Filename"].ToString();
        string decoded;
        try { decoded = Uri.UnescapeDataString(rawName); } catch (UriFormatException) { decoded = rawName; }
        var safeName = FileNameSanitizer.Sanitize(decoded);

        long? declared = long.TryParse(req.Headers["X-Size"], out var s) ? s : null;
        var max = Settings.MaxFileSizeMb * 1024 * 1024;
        if ((declared ?? req.ContentLength ?? 0) > max) return Error(413, "too_large", "File exceeds the size limit.");
        var expectedHash = req.Headers["X-Sha256"].ToString().Trim().ToLowerInvariant();
        if (expectedHash.Length > 0 && (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)))
            return Error(400, "bad_hash", "X-Sha256 must be 64 hex characters.");

        var dir = Settings.DownloadDir;
        Directory.CreateDirectory(dir);
        try
        {
            if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace < (declared ?? 0) + 100L * 1024 * 1024)
                return Error(507, "insufficient_storage", "Not enough free disk space on the PC.");
        }
        catch (ArgumentException) { /* network/unknown drive: skip the quota check */ }

        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = max;

        var tmp = Path.Combine(dir, $".{Guid.NewGuid():N}.partial");
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, useAsync: true))
            {
                int n;
                while ((n = await req.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
                {
                    total += n;
                    if (total > max) return Error(413, "too_large", "File exceeds the size limit.");
                    hasher.AppendData(buffer, 0, n);
                    await fs.WriteAsync(buffer.AsMemory(0, n), ctx.RequestAborted);
                }
            }
            if (declared is long d && d != total) return Error(400, "size_mismatch", "Received size differs from X-Size.");
            var sha = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (expectedHash.Length > 0 && sha != expectedHash) return Error(400, "hash_mismatch", "SHA-256 differs from X-Sha256.");

            var final = MoveToUniqueName(tmp, dir, safeName);
            MarkOfTheWeb(final);
            var transfer = Db.AddTransfer(new TransferRecord(0, "toPc", Path.GetFileName(final), total, sha, final, "done", Time.GetUtcNow()));
            _log.LogInformation("File received: {Size} bytes", total);
            Raise(FileReceived, transfer);
            return Results.Json(new FileUploadResponse(transfer.Name, total, sha), statusCode: 201);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == 413)
        {
            return Error(413, "too_large", "File exceeds the size limit.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(tmp)) File.Delete(tmp);   // aborted, rejected or failed upload
        }
    }

    static string MoveToUniqueName(string tmp, string dir, string safeName)
    {
        for (var attempt = 0; ; attempt++)
        {
            var path = FileNameSanitizer.UniquePath(dir, safeName);
            try
            {
                File.Move(tmp, path);
                return path;
            }
            catch (IOException) when (attempt < 20 && File.Exists(path)) { /* concurrent upload took the name */ }
        }
    }

    /// <summary>Zone.Identifier stream: Windows treats received files like downloads (SmartScreen, Office protected view).</summary>
    static void MarkOfTheWeb(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (IOException) { /* non-NTFS volume */ }
        catch (UnauthorizedAccessException) { }
    }

    IResult GetOutboxItem(string id)
    {
        var item = Outbox.Get(id);
        if (item is null) return Error(404, "not_found", "Unknown outbox item.");
        if (item.Kind != OutboxKinds.File) return Results.Json(item);
        if (item.Path is null || !File.Exists(item.Path)) return Error(404, "file_missing", "The file no longer exists on the PC.");
        return Results.File(item.Path, "application/octet-stream", item.Name, enableRangeProcessing: true);
    }

    IResult PostLink(LinkRequest req)
    {
        if (req.Url is not { Length: <= 8192 } || !Uri.TryCreate(req.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Error(400, "invalid_url", "Only http and https links are accepted.");
        Raise(LinkReceived, (uri, req.Title));
        return Results.NoContent();
    }

    void Raise(Action<Uri, string?>? handler, (Uri, string?) args)
    {
        try { handler?.Invoke(args.Item1, args.Item2); }
        catch (Exception e) { _log.LogError("Event handler failed: {Type}", e.GetType().Name); }
    }

    IResult PostClipboard(ClipboardPayload req)
    {
        var kind = req.Kind ?? "text";
        if (kind is "text" or "url")
        {
            if (string.IsNullOrEmpty(req.Text) || req.Text.Length > 1_000_000) return Error(400, "invalid_text", "Text is required (max 1 MB).");
        }
        else if (kind == "image")
        {
            if (string.IsNullOrEmpty(req.ImageBase64) || req.ImageBase64.Length > 28_000_000) return Error(400, "invalid_image", "imageBase64 is required (max 20 MB).");
            try { Convert.FromBase64String(req.ImageBase64); }
            catch (FormatException) { return Error(400, "invalid_image", "imageBase64 is not valid base64."); }
        }
        else return Error(400, "invalid_kind", "kind must be text, url or image.");

        var sensitive = OtpDetector.Detect(req.Text) is not null;
        var payload = req with { Kind = kind, UpdatedAt = Time.GetUtcNow() };
        if (Settings.KeepHistory && !sensitive && kind != "image")
            Db.AddClipboard(new ClipboardRecord(0, kind, req.Text, "phone", Time.GetUtcNow()));
        try { ClipboardReceived?.Invoke(payload, sensitive); }
        catch (Exception e) { _log.LogError("Event handler failed: {Type}", e.GetType().Name); }
        return Results.NoContent();
    }

    IResult PostMessage(MessageIn req)
    {
        if (string.IsNullOrWhiteSpace(req.Sender) || req.Sender.Length > 200) return Error(400, "invalid_sender", "sender is required.");
        if (req.Body is null || req.Body.Length > 20_000) return Error(400, "invalid_body", "body is required (max 20000 chars).");
        var service = req.Service?.ToLowerInvariant() is "imessage" ? "imessage" : "sms";
        var msg = new MessageRecord(0, req.Sender.Trim(), req.Body, service, "in", req.ReceivedAt ?? Time.GetUtcNow());
        if (Settings.KeepHistory) msg = Db.AddMessage(msg);
        _log.LogInformation("Message received");
        Raise(MessageReceived, msg);
        if (Settings.OtpAutoCopy && OtpDetector.Detect(req.Body) is { } code)
        {
            try { OtpDetected?.Invoke(code, "message"); }
            catch (Exception e) { _log.LogError("Event handler failed: {Type}", e.GetType().Name); }
        }
        return Results.NoContent();
    }

    IResult PostBattery(BatteryIn req)
    {
        if (req.Level is not { } level || level < 0 || level > 100) return Error(400, "invalid_level", "level must be 0-100.");
        var source = req.Source is "app" or "shortcut" ? req.Source : "shortcut";
        Battery = new BatteryState(level, req.Charging ?? false, source, Time.GetUtcNow());
        Raise(BatteryChanged, Battery);
        return Results.NoContent();
    }

    async Task Events(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            await Write(ctx, 400, "websocket_required", "Use a WebSocket upgrade.");
            return;
        }
        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        await _hub.RunAsync(socket, new OutboxEvent("outbox", Outbox.Pending().Count), ctx.RequestAborted);
    }
}
