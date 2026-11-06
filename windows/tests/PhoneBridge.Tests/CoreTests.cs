using PhoneBridge.Core;

namespace PhoneBridge.Tests;

sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan t) => Now += t;
}

sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pb-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData("../../Windows/System32/evil.dll", "evil.dll")]
    [InlineData("..\\..\\boot.ini", "boot.ini")]
    [InlineData("C:\\Users\\x\\a.txt", "a.txt")]
    [InlineData("a<b>c:d\"e|f?g*h.txt", "abcdefgh.txt")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("LPT1.log", "_LPT1.log")]
    [InlineData("COM9", "_COM9")]
    [InlineData("console.txt", "console.txt")]
    [InlineData("name. . .", "name")]
    [InlineData("..", "file")]
    [InlineData(".hidden", "hidden")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("   ", "file")]
    [InlineData("tab\there\u0000.txt", "tabhere.txt")]
    [InlineData("photo\u202Egpj.exe", "photogpj.exe")]
    [InlineData("Été à Rennes.heic", "Été à Rennes.heic")]
    public void Sanitizes(string? input, string expected) => Assert.Equal(expected, FileNameSanitizer.Sanitize(input));

    [Fact]
    public void TruncatesLongNamesKeepingExtension()
    {
        var name = FileNameSanitizer.Sanitize(new string('a', 500) + ".jpeg");
        Assert.True(name.Length <= 200);
        Assert.EndsWith(".jpeg", name);
    }

    [Fact]
    public void UniquePathAddsCounter()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, "a.txt"), FileNameSanitizer.UniquePath(dir.Path, "a.txt"));
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(dir.Path, "a (1).txt"), "");
        Assert.Equal(Path.Combine(dir.Path, "a (2).txt"), FileNameSanitizer.UniquePath(dir.Path, "a.txt"));
    }
}

public class OtpDetectorTests
{
    [Theory]
    [InlineData("Votre code de vérification est 482193", "482193")]
    [InlineData("Your verification code: 482 193", "482193")]
    [InlineData("G-123456 is your Google verification code.", "123456")]
    [InlineData("Code OTP : 4821. Ne le partagez pas.", "4821")]
    [InlineData("Ton code 2FA : 12345678", "12345678")]
    [InlineData("Utilisez le code 917-204 pour vous connecter", "917204")]
    [InlineData("VERIFICATION 5531", "5531")]
    public void DetectsCodes(string text, string expected) => Assert.Equal(expected, OtpDetector.Detect(text));

    [Theory]
    [InlineData("On se voit à 14h30 ?")]
    [InlineData("Appelle-moi au 0612345678")]
    [InlineData("Ta commande 123456 est expédiée")]        // no keyword
    [InlineData("Code promo SOLDES2024")]                 // digits glued to letters
    [InlineData("Le code de la porte a changé")]           // keyword, no digits
    [InlineData("Votre code : 123")]                       // too short
    [InlineData("Montant du code cadeau : 1500 €")]        // amount
    [InlineData("")]
    [InlineData(null)]
    public void IgnoresNonCodes(string? text) => Assert.Null(OtpDetector.Detect(text));

    [Fact]
    public void FormatsForDisplay()
    {
        Assert.Equal("482 193", OtpDetector.Format("482193"));
        Assert.Equal("4821", OtpDetector.Format("4821"));
    }
}

public class PairingManagerTests
{
    [Fact]
    public void SecretIs32BytesBase64Url()
    {
        var offer = new PairingManager().CreateOffer();
        Assert.Equal(43, offer.Secret.Length);
        Assert.DoesNotContain('+', offer.Secret);
        Assert.DoesNotContain('/', offer.Secret);
    }

    [Fact]
    public void SecretIsSingleUse()
    {
        var p = new PairingManager(new FakeTime());
        var offer = p.CreateOffer();
        Assert.Equal(PairOutcome.Ok, p.TryRedeem(offer.Secret));
        Assert.Equal(PairOutcome.Expired, p.TryRedeem(offer.Secret));
    }

    [Fact]
    public void SecretExpiresAfterFiveMinutes()
    {
        var time = new FakeTime();
        var p = new PairingManager(time);
        var offer = p.CreateOffer();
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(PairOutcome.Expired, p.TryRedeem(offer.Secret));
    }

    [Fact]
    public void SecretValidJustBeforeExpiry()
    {
        var time = new FakeTime();
        var p = new PairingManager(time);
        var offer = p.CreateOffer();
        time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.Equal(PairOutcome.Ok, p.TryRedeem(offer.Secret));
    }

    [Fact]
    public void WrongOrReplacedSecretIsInvalid()
    {
        var p = new PairingManager(new FakeTime());
        Assert.Equal(PairOutcome.Invalid, p.TryRedeem("anything"));
        var first = p.CreateOffer();
        p.CreateOffer();
        Assert.Equal(PairOutcome.Invalid, p.TryRedeem(first.Secret));
        Assert.Equal(PairOutcome.Invalid, p.TryRedeem(null));
        Assert.Equal(PairOutcome.Invalid, p.TryRedeem(""));
    }
}

public class RateLimiterTests
{
    [Fact]
    public void LimitsPerKeyWithinWindow()
    {
        var time = new FakeTime();
        var limiter = new RateLimiter(5, TimeSpan.FromMinutes(1), time);
        for (var i = 0; i < 4; i++) limiter.Hit("1.2.3.4");
        Assert.False(limiter.IsLimited("1.2.3.4"));
        limiter.Hit("1.2.3.4");
        Assert.True(limiter.IsLimited("1.2.3.4"));
        Assert.False(limiter.IsLimited("5.6.7.8"));
    }

    [Fact]
    public void WindowSlides()
    {
        var time = new FakeTime();
        var limiter = new RateLimiter(2, TimeSpan.FromMinutes(1), time);
        limiter.Hit("ip");
        time.Advance(TimeSpan.FromSeconds(30));
        limiter.Hit("ip");
        Assert.True(limiter.IsLimited("ip"));
        time.Advance(TimeSpan.FromSeconds(31));   // first hit leaves the window
        Assert.False(limiter.IsLimited("ip"));
    }
}

public class ReplyQueueTests
{
    sealed class FakeChannel(string name, Func<bool> behavior) : IReplyChannel
    {
        public string Name => name;
        public int Calls;
        public Task<bool> TrySendAsync(string to, string body, CancellationToken ct) { Calls++; return Task.FromResult(behavior()); }
    }

    [Fact]
    public async Task QueuesForRelayWhenNoChannel()
    {
        using var dir = new TempDir();
        var db = new Database(Path.Combine(dir.Path, "t.db"));
        var outbox = new OutboxQueue(db);
        var replies = new ReplyQueue(db, outbox);

        var msg = await replies.SendAsync("+33600000000", "J'arrive");

        Assert.Equal(SendStatus.Queued, msg.Status);
        Assert.Equal(SendStatus.Queued, db.GetMessage(msg.Id)!.Status);
        var item = Assert.Single(outbox.Pending());
        Assert.Equal(OutboxKinds.Reply, item.Kind);
        Assert.Equal("+33600000000", item.To);
        Assert.Equal("J'arrive", item.Text);
    }

    [Fact]
    public async Task RelayAckMarksSent()
    {
        using var dir = new TempDir();
        var db = new Database(Path.Combine(dir.Path, "t.db"));
        var outbox = new OutboxQueue(db);
        var replies = new ReplyQueue(db, outbox);
        MessageRecord? changed = null;
        replies.StatusChanged += m => changed = m;

        var msg = await replies.SendAsync("+33600000000", "ok");
        Assert.True(outbox.Acknowledge(outbox.Pending()[0].Id));

        Assert.Equal(SendStatus.Sent, db.GetMessage(msg.Id)!.Status);
        Assert.Equal("relay", changed?.Via);
        Assert.Empty(outbox.Pending());
        Assert.False(outbox.Acknowledge(msg.Id.ToString()));
    }

    [Fact]
    public async Task UsesFirstWorkingChannelAndSkipsBrokenOnes()
    {
        using var dir = new TempDir();
        var db = new Database(Path.Combine(dir.Path, "t.db"));
        var outbox = new OutboxQueue(db);
        var broken = new FakeChannel("broken", () => throw new IOException("link lost"));
        var off = new FakeChannel("off", () => false);
        var bt = new FakeChannel("bluetooth", () => true);
        var replies = new ReplyQueue(db, outbox, [broken, off, bt]);

        var msg = await replies.SendAsync("+33600000000", "salut");

        Assert.Equal(SendStatus.Sent, msg.Status);
        Assert.Equal("bluetooth", db.GetMessage(msg.Id)!.Via);
        Assert.Equal(1, broken.Calls);
        Assert.Equal(1, off.Calls);
        Assert.Empty(outbox.Pending());
    }

    [Fact]
    public async Task RejectsEmptyReplies()
    {
        using var dir = new TempDir();
        var db = new Database(Path.Combine(dir.Path, "t.db"));
        var replies = new ReplyQueue(db, new OutboxQueue(db));
        await Assert.ThrowsAsync<ArgumentException>(() => replies.SendAsync("", "x"));
        await Assert.ThrowsAsync<ArgumentException>(() => replies.SendAsync("+33", " "));
    }
}

public class DatabaseTests
{
    [Fact]
    public void ConversationHeadsReturnLatestPerPeer()
    {
        using var dir = new TempDir();
        var db = new Database(Path.Combine(dir.Path, "t.db"));
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        db.AddMessage(new MessageRecord(0, "A", "a1", "sms", "in", t));
        db.AddMessage(new MessageRecord(0, "B", "b1", "sms", "in", t.AddMinutes(1)));
        db.AddMessage(new MessageRecord(0, "A", "a2", "sms", "in", t.AddMinutes(2)));

        var heads = db.ListConversationHeads();
        Assert.Equal(["a2", "b1"], heads.Select(h => h.Body));
        Assert.Equal(["a1", "a2"], db.ListMessages("A").Select(m => m.Body));
    }

    [Fact]
    public void SettingsRoundTripAndKeepDeviceId()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var first = AppSettings.Load(path);
        first.WindowStyle = "windows";
        first.Save(path);
        var again = AppSettings.Load(path);
        Assert.Equal("windows", again.WindowStyle);
        Assert.Equal(first.DeviceId, again.DeviceId);
    }
}
