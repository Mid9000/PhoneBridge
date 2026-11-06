using System.Text;
using Microsoft.Data.Sqlite;
using PhoneBridge.Bluetooth.Ancs;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Obex;
using PhoneBridge.Bluetooth.Pbap;
using PhoneBridge.Bluetooth.Tests.Fakes;
using PhoneBridge.Core;

namespace PhoneBridge.Bluetooth.Tests;

/// <summary>Fake platform: ANCS, MAS and PSE simulators over loopback, or a fixed failure.</summary>
sealed class FakeLinks : IIphoneLinks
{
    private readonly List<IAsyncDisposable> _links = [];
    public FakeAncsIphone? Ancs { get; init; }
    public FakeIphoneMas? Mas { get; init; }
    public string? Vcards { get; init; }
    public Exception? Failure { get; init; }
    public MnsServer? Mns { get; private set; }
    public int Disposed { get; private set; }

    public Task<IAncsClient> OpenAncsAsync(CancellationToken ct) =>
        Failure is not null ? Task.FromException<IAncsClient>(Failure)
        : Ancs is null ? throw new BluetoothLinkException(BluetoothIssue.Unreachable, "no ANCS")
        : Task.FromResult<IAncsClient>(new AncsSession(Ancs));

    public async Task<IMapClient> OpenMapAsync(MnsServer mns, CancellationToken ct)
    {
        if (Failure is not null) throw Failure;
        if (Mas is null) throw new BluetoothLinkException(BluetoothIssue.Unreachable, "no MAS");
        Mns = mns;
        var link = await Loopback.CreateAsync();
        _links.Add(link);
        _ = Mas.RunAsync(link.Server);
        var map = new MapClient(_ => Task.FromResult(link.Client));
        map.AttachMns(mns);
        return map;
    }

    public async Task<IPbapClient> OpenPbapAsync(CancellationToken ct)
    {
        if (Failure is not null) throw Failure;
        if (Vcards is null) throw new BluetoothLinkException(BluetoothIssue.Unreachable, "no PSE");
        var link = await Loopback.CreateAsync();
        _links.Add(link);
        _ = new ObexServer(link.Server, (r, _) => Task.FromResult(r.Opcode switch
        {
            ObexOpcode.Connect => ObexServerReply.Ok(ObexHeader.ConnectionId(1)),
            ObexOpcode.Get => new ObexServerReply { Body = Encoding.UTF8.GetBytes(Vcards) },
            _ => ObexServerReply.Ok(),
        })).RunAsync();
        return new PbapClient(_ => Task.FromResult(link.Client));
    }

    /// <summary>The iPhone opens its MNS connection to the PC and pushes one event report.</summary>
    public async Task PushMnsEventAsync(string type, string handle)
    {
        await using var link = await Loopback.CreateAsync();
        var serve = Mns!.RunConnectionAsync(link.Server);
        var iphone = new ObexClient(link.Client);
        Assert.True((await iphone.ConnectAsync(BluetoothIds.MnsTarget)).IsSuccess);
        var report = $"<MAP-event-report version=\"1.0\"><event type=\"{type}\" handle=\"{handle}\" folder=\"TELECOM/MSG/INBOX\" msg_type=\"SMS_GSM\"/></MAP-event-report>";
        Assert.True((await iphone.PutAsync([ObexHeader.Type(MapTypes.EventReport)], Encoding.UTF8.GetBytes(report))).IsSuccess);
        await iphone.DisconnectAsync();
        await serve.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        Disposed++;
        foreach (var l in _links) await l.DisposeAsync();
        _links.Clear();
    }
}

public sealed class BluetoothServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pb-bt-" + Guid.NewGuid().ToString("N"));
    private readonly Database _db;
    private readonly AppSettings _settings = new();

    public BluetoothServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "t.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private BluetoothService Service(FakeLinks links) => new(links, _db, _settings) { RetryInterval = TimeSpan.FromHours(1) };

    private static async Task<T> Next<T>(Action<Action<T>> subscribe)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(v => tcs.TrySetResult(v));
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task No_iphone_never_throws_and_reports_issue()
    {
        var links = new FakeLinks { Failure = new BluetoothLinkException(BluetoothIssue.NotPaired, "none") };
        await using var bt = Service(links);
        Assert.Equal("off", bt.State);
        Assert.Null(bt.Issue);

        bt.Start();
        await bt.EnsureConnectedAsync();
        Assert.Equal("unavailable", bt.State);
        Assert.Equal(BluetoothIssue.NotPaired, bt.Issue);
        Assert.False(await bt.TrySendAsync("+33612345678", "x", default));
        Assert.False(await bt.PerformActionAsync(1, true));
        await bt.MarkConversationReadAsync("+33612345678");   // no-op, no throw

        await bt.StopAsync();
        Assert.Equal("off", bt.State);
        Assert.True(links.Disposed >= 1);
    }

    [Fact]
    public async Task Unexpected_platform_exception_becomes_unreachable()
    {
        await using var bt = Service(new FakeLinks { Failure = new System.Runtime.InteropServices.COMException("radio") });
        bt.Start();
        await bt.EnsureConnectedAsync();
        Assert.Equal(BluetoothIssue.Unreachable, bt.Issue);
    }

    [Fact]
    public async Task Ancs_notification_is_stored_toasted_once_and_removed()
    {
        var phone = new FakeAncsIphone();
        phone.AddExisting(new FakeNotification(1, AncsCategory.Social, "net.whatsapp.WhatsApp", "Old", "", "Ancien"));
        await using var bt = Service(new FakeLinks { Ancs = phone });
        var added = new List<PhoneNotice>();
        var preexisting = Next<PhoneNotice>(h => bt.NotificationAdded += h);
        bt.NotificationAdded += n => { lock (added) added.Add(n); };
        bt.Start();
        await bt.EnsureConnectedAsync();
        Assert.True((await preexisting).Silent);   // pre-existing: listed, never toasted

        var fresh = Next<PhoneNotice>(h => bt.NotificationAdded += n => { if (n.Uid == 2) h(n); });
        var otp = Next<string>(h => bt.OtpDetected += h);
        phone.Post(new FakeNotification(2, AncsCategory.Social, "net.whatsapp.WhatsApp", "Léa", "Groupe", "Ton code de vérification : 482193",
            Flags: AncsEventFlags.NegativeAction, Negative: "Effacer"));
        var n = await fresh;
        Assert.False(n.Silent);
        Assert.Equal(("social", "WhatsApp", "Groupe", "Effacer"), (n.Category, n.AppName, n.Subtitle, n.NegativeAction));
        Assert.Equal("482193", await otp);
        Assert.Contains(_db.ListNotifications(), r => r.Id == n.Id && r.Title == "Léa");
        Assert.Equal("connected", bt.State);

        Assert.True(await bt.PerformActionAsync(n.Id, positive: false));
        Assert.Contains((2u, AncsAction.Negative), phone.Actions);

        var removed = Next<PhoneNotice>(h => bt.NotificationRemoved += h);
        phone.Remove(2);
        Assert.Equal(n.Id, (await removed).Id);
        Assert.DoesNotContain(_db.ListNotifications(), r => r.Id == n.Id);
        Assert.Null(bt.Find(n.Id));
    }

    [Fact]
    public async Task Incoming_call_can_be_answered()
    {
        var phone = new FakeAncsIphone();
        await using var bt = Service(new FakeLinks { Ancs = phone });
        bt.Start();
        await bt.EnsureConnectedAsync();
        var call = Next<PhoneNotice>(h => bt.IncomingCall += h);
        phone.Post(new FakeNotification(9, AncsCategory.IncomingCall, "com.apple.mobilephone", "Maman", "", "",
            Flags: AncsEventFlags.PositiveAction | AncsEventFlags.NegativeAction, Positive: "Répondre", Negative: "Refuser"));
        var c = await call;
        Assert.True(c.Ringing);
        Assert.Equal("call", c.Category);
        Assert.Single(bt.RingingCalls());
        Assert.True(await bt.PerformActionAsync(c.Id, positive: true));
        Assert.Equal((9u, AncsAction.Positive), phone.Actions.Single());
    }

    [Fact]
    public async Task Ancs_access_denied_is_reported_as_toggle_issue()
    {
        await using var bt = Service(new FakeLinks { Ancs = new FakeAncsIphone { Bonded = false } });
        bt.Start();
        await bt.EnsureConnectedAsync();
        Assert.Equal(BluetoothIssue.NotificationsDenied, bt.Issue);
    }

    [Fact]
    public async Task Map_message_is_stored_named_and_marked_read_from_pc()
    {
        var mas = new FakeIphoneMas();
        mas.Inbox.Add(new FakeMessage("20000001", "Léa Martin", "+33611223344", "Salut, code 482193 pour ton compte", "20261106T100000", false));
        var links = new FakeLinks { Mas = mas, Vcards = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Léa M.\r\nTEL:06 11 22 33 44\r\nEND:VCARD\r\n" };
        await using var bt = Service(links);
        bt.Start();
        await bt.EnsureConnectedAsync();
        Assert.True(mas.NotificationsOn);
        Assert.Equal("Léa M.", bt.Contacts.NameFor("+33611223344"));   // PBAP name, matched across formats

        var received = Next<(MessageRecord, bool)>(h => bt.MessageReceived += (m, unread) => h((m, unread)));
        await links.PushMnsEventAsync("NewMessage", "20000001");
        var (msg, unread) = await received;
        Assert.True(unread);
        Assert.Equal(("+33611223344", "in", "sms"), (msg.Peer, msg.Direction, msg.Service));
        Assert.Equal("Salut, code 482193 pour ton compte", _db.GetMessage(msg.Id)!.Body);
        Assert.False(mas.Inbox[0].Read);   // receiving must not mark read

        await bt.MarkConversationReadAsync("+33611223344");
        Assert.True(mas.Inbox[0].Read);
    }

    [Fact]
    public async Task Read_on_iphone_raises_conversation_read()
    {
        var mas = new FakeIphoneMas();
        mas.Inbox.Add(new FakeMessage("20000001", "Léa", "+33611223344", "Coucou", "20261106T100000", false));
        var links = new FakeLinks { Mas = mas };
        await using var bt = Service(links);
        bt.Start();
        await bt.EnsureConnectedAsync();
        var received = Next<MessageRecord>(h => bt.MessageReceived += (m, _) => h(m));
        await links.PushMnsEventAsync("NewMessage", "20000001");
        await received;

        var read = Next<string>(h => bt.ConversationRead += h);
        await links.PushMnsEventAsync("ReadStatusChanged", "20000001");
        Assert.Equal("+33611223344", await read);
    }

    [Fact]
    public async Task Replies_go_over_map_when_connected_else_relay()
    {
        var mas = new FakeIphoneMas();
        await using var bt = Service(new FakeLinks { Mas = mas });
        var outbox = new OutboxQueue(_db);
        var replies = new ReplyQueue(_db, outbox, [bt]);

        var before = await replies.SendAsync("+33612345678", "avant");   // not started: honest "queued"
        Assert.Equal((SendStatus.Queued, "relay"), (before.Status, before.Via));

        bt.Start();
        await bt.EnsureConnectedAsync();
        var sent = await replies.SendAsync("+33 6 12 34 56 78", "Bonjour 👋");
        Assert.Equal((SendStatus.Sent, "bluetooth"), (sent.Status, sent.Via));
        Assert.Equal("Bonjour 👋", BMessage.Parse(mas.PushedBodies.Single()).Body);

        var byName = await replies.SendAsync("Maman", "salut");   // a name, not an address: MAP cannot send it
        Assert.Equal(SendStatus.Queued, byName.Status);
        Assert.Single(mas.PushedBodies);
    }

    [Fact]
    public async Task Map_forbidden_reports_issue_and_replies_are_queued()
    {
        await using var bt = Service(new FakeLinks { Mas = new FakeIphoneMas { ConnectResult = ObexResponseCode.Forbidden } });
        bt.Start();
        await bt.EnsureConnectedAsync();
        Assert.Equal(BluetoothIssue.MessagesDenied, bt.Issue);
        var reply = await new ReplyQueue(_db, new OutboxQueue(_db), [bt]).SendAsync("+33612345678", "x");
        Assert.Equal(SendStatus.Queued, reply.Status);
    }

    [Fact]
    public async Task Messages_app_notifications_are_silent_when_map_delivers()
    {
        var ancs = new FakeAncsIphone();
        await using var bt = Service(new FakeLinks { Ancs = ancs, Mas = new FakeIphoneMas() });
        bt.Start();
        await bt.EnsureConnectedAsync();
        var n = Next<PhoneNotice>(h => bt.NotificationAdded += h);
        ancs.Post(new FakeNotification(3, AncsCategory.Social, "com.apple.MobileSMS", "Léa", "", "Coucou"));
        var notice = await n;
        Assert.True(notice.Silent);
        Assert.Equal("message", notice.Category);
    }

    [Theory]
    [InlineData("+33 6 12 34 56 78", true)]
    [InlineData("lea@icloud.com", true)]
    [InlineData("Maman", false)]
    [InlineData("12", false)]
    public void Address_check(string to, bool expected) => Assert.Equal(expected, BluetoothService.LooksLikeAddress(to));
}

public class ContactBookTests
{
    [Fact]
    public void Matches_numbers_across_formats_and_emails_case_insensitively()
    {
        var book = new ContactBook();
        book.Load(VCard.ParseMany("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Léa\r\nTEL:+33 6 11 22 33 44\r\nEMAIL:Lea@iCloud.com\r\nEND:VCARD\r\n" +
                                  "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:\r\nTEL:+33600000000\r\nEND:VCARD\r\n"));
        Assert.Equal("Léa", book.NameFor("0611223344"));
        Assert.Equal("Léa", book.NameFor("lea@icloud.com"));
        Assert.Null(book.NameFor("+33600000000"));   // nameless card: no "name" equal to the number
        book.Learn("+33611223344", "Autre");
        Assert.Equal("Léa", book.NameFor("+33611223344"));   // PBAP wins over MAP sender names
        book.Learn("+33700000000", "Paul");
        Assert.Equal("Paul", book.NameFor("07 00 00 00 00"));
    }
}
