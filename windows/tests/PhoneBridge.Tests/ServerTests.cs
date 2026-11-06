using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PhoneBridge.Core;
using PhoneBridge.Core.Protocol;
using PhoneBridge.Server;

namespace PhoneBridge.Tests;

/// <summary>A real Kestrel server on free ports, with a pinned-certificate HTTPS client.</summary>
sealed class LiveServer : IAsyncDisposable
{
    public readonly TempDir Dir = new();
    public readonly BridgeServer Server;
    public readonly HttpClient Https;
    public readonly HttpClient Http;
    public string DownloadDir => Server.Settings.DownloadDir;

    LiveServer(BridgeServer server)
    {
        Server = server;
        var handler = new HttpClientHandler
        {
            // Pinning exactly like the iPhone: accept only the advertised fingerprint.
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && CertificateManager.Sha256(cert) == server.CertSha256,
        };
        Https = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{server.HttpsPort}") };
        Http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.HttpPort}") };
    }

    public static async Task<LiveServer> StartAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pb-srv-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { DownloadDir = Path.Combine(dir, "downloads"), PcName = "PC test", MaxFileSizeMb = 50 };
        var server = new BridgeServer(new ServerOptions { DataDir = dir, HttpsPort = 0, HttpPort = 0, AdvertiseMdns = false },
            settings, new InMemorySecretStore());
        await server.StartAsync();
        return new LiveServer(server);
    }

    public async Task<string> PairAsync()
    {
        var qr = Server.CreatePairingPayload();
        var res = await Https.PostAsJsonAsync("/v1/pair", new PairRequest(qr.PairingSecret, "iPhone test", Guid.NewGuid().ToString()));
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<PairResponse>())!.Token;
        Https.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    public async ValueTask DisposeAsync()
    {
        Https.Dispose();
        Http.Dispose();
        await Server.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetDirectoryName(DownloadDir)!, true); } catch (IOException) { }
        Dir.Dispose();
    }
}

public class ServerIntegrationTests
{
    static HttpRequestMessage Upload(byte[] data, string name, string? sha = null, long? size = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/files") { Content = new ByteArrayContent(data) };
        req.Headers.Add("X-Filename", Uri.EscapeDataString(name));
        req.Headers.Add("X-Size", (size ?? data.Length).ToString());
        if (sha is not null) req.Headers.Add("X-Sha256", sha);
        return req;
    }

    static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public async Task PairingIsSingleUseAndRateLimited()
    {
        await using var s = await LiveServer.StartAsync();
        var qr = s.Server.CreatePairingPayload("192.168.1.20");
        Assert.Equal(64, qr.CertSha256.Length);
        Assert.Equal(s.Server.HttpsPort, qr.Port);

        var ok = await s.Https.PostAsJsonAsync("/v1/pair", new PairRequest(qr.PairingSecret, "iPhone", null));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = (await ok.Content.ReadFromJsonAsync<PairResponse>())!;
        Assert.Equal(43, body.Token.Length);
        Assert.Equal(s.Server.HttpPort, body.HttpPort);
        Assert.Single(s.Server.Db.ListDevices());

        var reused = await s.Https.PostAsJsonAsync("/v1/pair", new PairRequest(qr.PairingSecret, "iPhone", null));
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);

        // 1 failure so far; 4 more wrong secrets reach the 5/min limit, the next call is throttled.
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.Https.PostAsJsonAsync("/v1/pair", new PairRequest("wrong", null, null))).StatusCode);
        var limited = await s.Https.PostAsJsonAsync("/v1/pair", new PairRequest(s.Server.CreatePairingPayload().PairingSecret, null, null));
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
    }

    [Fact]
    public async Task RequestsWithoutValidTokenAreRejected()
    {
        await using var s = await LiveServer.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Https.GetAsync("/v1/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.GetAsync("/v1/outbox")).StatusCode);

        var bad = new HttpRequestMessage(HttpMethod.Get, "/v1/outbox");
        bad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        var res = await s.Https.SendAsync(bad);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("unauthorized", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());

        // Anonymous status reveals only name + version.
        var status = JsonDocument.Parse(await s.Https.GetStringAsync("/v1/status")).RootElement;
        Assert.Equal("PC test", status.GetProperty("name").GetString());
        Assert.False(status.TryGetProperty("features", out _));

        var token = await s.PairAsync();
        status = JsonDocument.Parse(await s.Https.GetStringAsync("/v1/status")).RootElement;
        Assert.True(status.GetProperty("features").GetProperty("shortcutsHttp").GetBoolean());

        Assert.True(s.Server.RevokeDevice(s.Server.Db.ListDevices()[0].Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Https.GetAsync("/v1/outbox")).StatusCode);
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task AuthFailuresAreRateLimited()
    {
        await using var s = await LiveServer.StartAsync();
        for (var i = 0; i < 10; i++) await s.Https.GetAsync("/v1/outbox");
        await s.PairAsync();   // /pair stays reachable, but the IP is now throttled on other routes
        Assert.Equal((HttpStatusCode)429, (await s.Https.GetAsync("/v1/outbox")).StatusCode);
    }

    [Fact]
    public async Task UploadVerifiesHashAndSanitizesName()
    {
        await using var s = await LiveServer.StartAsync();
        await s.PairAsync();
        TransferRecord? received = null;
        s.Server.FileReceived += t => received = t;
        var data = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 17);

        var res = await s.Https.SendAsync(Upload(data, "../../Windows/évil:name.jpg", Hex(data)));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<FileUploadResponse>())!;
        Assert.Equal("évilname.jpg", body.SavedAs);
        Assert.Equal(Hex(data), body.Sha256);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(s.DownloadDir, "évilname.jpg")));
        Assert.Equal(body.SavedAs, received?.Name);

        var second = await s.Https.SendAsync(Upload(data, "évil:name.jpg"));
        Assert.Equal("évilname (1).jpg", (await second.Content.ReadFromJsonAsync<FileUploadResponse>())!.SavedAs);

        var wrongHash = await s.Https.SendAsync(Upload(data, "bad.bin", new string('0', 64)));
        Assert.Equal(HttpStatusCode.BadRequest, wrongHash.StatusCode);
        var wrongSize = await s.Https.SendAsync(Upload(data, "bad.bin", size: data.Length + 1));
        Assert.Equal(HttpStatusCode.BadRequest, wrongSize.StatusCode);
        Assert.Equal(["évilname (1).jpg", "évilname.jpg"], Directory.GetFiles(s.DownloadDir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task UploadOverLimitIsRejected()
    {
        await using var s = await LiveServer.StartAsync();
        await s.PairAsync();
        s.Server.Settings.MaxFileSizeMb = 1;
        var res = await s.Http.SendAsync(Upload(new byte[2 * 1024 * 1024], "big.bin"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        Assert.True(!Directory.Exists(s.DownloadDir) || Directory.GetFiles(s.DownloadDir).Length == 0);
    }

    [Fact]
    public async Task OutboxFileSupportsRangeAndAck()
    {
        await using var s = await LiveServer.StartAsync();
        await s.PairAsync();
        var data = RandomNumberGenerator.GetBytes(10_000);
        var path = Path.Combine(s.Dir.Path, "to-phone.bin");
        await File.WriteAllBytesAsync(path, data);
        var item = s.Server.Outbox.EnqueueFile(path);

        var list = JsonDocument.Parse(await s.Https.GetStringAsync("/v1/outbox")).RootElement.GetProperty("items");
        Assert.Equal(item.Id, list[0].GetProperty("id").GetString());
        Assert.Equal("file", list[0].GetProperty("kind").GetString());
        Assert.False(list[0].TryGetProperty("path", out _));   // local paths never leave the PC

        var range = new HttpRequestMessage(HttpMethod.Get, $"/v1/outbox/{item.Id}");
        range.Headers.Range = new RangeHeaderValue(100, 199);
        var partial = await s.Https.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(data[100..200], await partial.Content.ReadAsByteArrayAsync());

        var resume = new HttpRequestMessage(HttpMethod.Get, $"/v1/outbox/{item.Id}");
        resume.Headers.Range = new RangeHeaderValue(9_000, null);
        Assert.Equal(data[9_000..], await (await s.Http.SendAsync(resume)).Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await s.Https.DeleteAsync($"/v1/outbox/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Https.GetAsync($"/v1/outbox/{item.Id}")).StatusCode);
        Assert.Equal("done", s.Server.Db.GetTransfer(item.TransferId!.Value)!.Status);
    }

    [Fact]
    public async Task HttpPortServesOnlyShortcutRoutes()
    {
        await using var s = await LiveServer.StartAsync();
        var secret = s.Server.CreatePairingPayload().PairingSecret;
        Assert.Equal(HttpStatusCode.NotFound, (await s.Http.PostAsJsonAsync("/v1/pair", new PairRequest(secret, null, null))).StatusCode);
        await s.PairAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Http.GetAsync("/v1/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Http.PostAsJsonAsync("/v1/battery", new BatteryIn(42, true, "shortcut"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Http.GetAsync("/v1/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Http.GetAsync("/v1/nope")).StatusCode);
        Assert.Equal(42, s.Server.Battery?.Level);

        var port = s.Server.HttpPort!.Value;
        await s.Server.SetShortcutsHttpAsync(false);
        await Assert.ThrowsAsync<HttpRequestException>(() => new HttpClient().GetAsync($"http://127.0.0.1:{port}/v1/status"));
    }

    [Theory]
    [InlineData("8.8.8.8", 403)]
    [InlineData("100.64.0.1", 403)]
    [InlineData("2001:4860:4860::8888", 403)]
    [InlineData("192.168.1.30", 401)]
    [InlineData("10.0.0.2", 401)]
    [InlineData("172.20.10.2", 401)]    // iPhone personal hotspot
    [InlineData("::ffff:192.168.1.30", 401)]
    [InlineData("fe80::1", 401)]
    public async Task PublicAddressesAreRefused(string ip, int expected)
    {
        await using var s = await LiveServer.StartAsync();
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Request.Path = "/v1/outbox";
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, null, "test"));
        ctx.Response.Body = new MemoryStream();
        var reached = false;
        await s.Server.Gate(ctx, _ => { reached = true; return Task.CompletedTask; });
        Assert.Equal(expected, ctx.Response.StatusCode);
        Assert.False(reached);
    }

    [Fact]
    public async Task MessageWithCodeRaisesOtpAndStoresHistory()
    {
        await using var s = await LiveServer.StartAsync();
        await s.PairAsync();
        string? otp = null;
        MessageRecord? msg = null;
        s.Server.OtpDetected += (code, _) => otp = code;
        s.Server.MessageReceived += m => msg = m;

        var res = await s.Http.PostAsJsonAsync("/v1/messages",
            new MessageIn("+33612345678", "Votre code de vérification : 482 193", "sms", DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("482193", otp);
        Assert.Equal("+33612345678", msg?.Peer);
        Assert.Single(s.Server.Db.ListMessages("+33612345678"));

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Http.PostAsJsonAsync("/v1/messages", new MessageIn("", "x", null, null))).StatusCode);
    }

    [Fact]
    public async Task LinksAndClipboard()
    {
        await using var s = await LiveServer.StartAsync();
        await s.PairAsync();
        Uri? opened = null;
        (ClipboardPayload Payload, bool Sensitive)? clip = null;
        s.Server.LinkReceived += (u, _) => opened = u;
        s.Server.ClipboardReceived += (p, sensitive) => clip = (p, sensitive);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Http.PostAsJsonAsync("/v1/links", new LinkRequest("javascript:alert(1)", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Http.PostAsJsonAsync("/v1/links", new LinkRequest("file:///C:/Windows", null))).StatusCode);
        Assert.Null(opened);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Http.PostAsJsonAsync("/v1/links", new LinkRequest("https://example.org/a?b=1", "t"))).StatusCode);
        Assert.Equal("https://example.org/a?b=1", opened?.ToString());

        Assert.Equal(HttpStatusCode.NoContent, (await s.Http.PostAsJsonAsync("/v1/clipboard", new ClipboardPayload("text", "bonjour", null))).StatusCode);
        Assert.Equal(("bonjour", false), (clip?.Payload.Text, clip?.Sensitive));
        Assert.Equal(HttpStatusCode.NoContent, (await s.Http.PostAsJsonAsync("/v1/clipboard", new ClipboardPayload("text", "Ton code OTP 123456", null))).StatusCode);
        Assert.True(clip?.Sensitive);
        Assert.Equal(["bonjour"], s.Server.Db.ListClipboard().Select(c => c.Text));   // OTP never stored

        Assert.Equal(HttpStatusCode.NoContent, (await s.Http.GetAsync("/v1/clipboard")).StatusCode);
        s.Server.PublishPcClipboard("text", "depuis le PC", null, sensitive: false);
        var pc = (await s.Http.GetFromJsonAsync<ClipboardPayload>("/v1/clipboard"))!;
        Assert.Equal("depuis le PC", pc.Text);
    }

    [Fact]
    public async Task EventsWebSocketPushesOutboxCount()
    {
        await using var s = await LiveServer.StartAsync();
        var token = await s.PairAsync();
        using var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) =>
            cert is not null && CertificateManager.Sha256(new X509Certificate2(cert)) == s.Server.CertSha256;
        await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{s.Server.HttpsPort}/v1/events?token={token}"), CancellationToken.None);

        Assert.Equal("""{"type":"outbox","count":0}""", await Receive(ws));
        s.Server.Outbox.EnqueueLink("https://example.org");
        Assert.Equal("""{"type":"outbox","count":1}""", await Receive(ws));
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    static async Task<string> Receive(ClientWebSocket ws)
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var r = await ws.ReceiveAsync(buffer, cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, r.Count);
    }
}
