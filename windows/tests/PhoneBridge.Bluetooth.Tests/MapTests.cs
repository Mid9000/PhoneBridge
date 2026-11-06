using System.Text;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Obex;
using PhoneBridge.Bluetooth.Tests.Fakes;

namespace PhoneBridge.Bluetooth.Tests;

public class MapXmlTests
{
    // Example shaped after MAP 1.4 §3.1.6.
    private const string Listing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MAP-msg-listing version="1.0">
          <msg handle="20000100001" subject="Salut &amp; bisous" datetime="20261106T130510" sender_name="Jamie"
               sender_addressing="+33612345678" recipient_addressing="" type="SMS_GSM" size="256"
               attachment_size="0" priority="no" read="yes" sent="no" protected="no"/>
          <msg handle="20000100002" subject="Hi" datetime="20261106T130511+0100" type="SMS_GSM" read="no"/>
        </MAP-msg-listing>
        """;

    [Fact]
    public void Messages_listing_parses_attributes()
    {
        var list = MapXml.ParseMessagesListing(Listing);
        Assert.Equal(2, list.Count);
        var m = list[0];
        Assert.Equal("20000100001", m.Handle);
        Assert.Equal("Salut & bisous", m.Subject);
        Assert.Equal("Jamie", m.SenderName);
        Assert.Equal("+33612345678", m.SenderAddressing);
        Assert.Equal("SMS_GSM", m.Type);
        Assert.Equal(256, m.Size);
        Assert.True(m.Read);
        Assert.False(m.Sent);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 5, 10), m.DateTime!.Value.DateTime);
        Assert.Equal(TimeSpan.FromHours(1), list[1].DateTime!.Value.Offset);
        Assert.False(list[1].Read);
    }

    [Fact]
    public void Malformed_xml_falls_back_to_lenient_scan()
    {
        const string broken = "<MAP-msg-listing version=\"1.0\"><msg handle=\"1\" subject=\"Tom & Jerry <3\" read=\"yes\"/><msg handle='2' subject='x'/>";
        var list = MapXml.ParseMessagesListing(broken);
        Assert.Equal(["1", "2"], list.Select(m => m.Handle));
        Assert.Equal("Tom & Jerry <3", list[0].Subject);
    }

    [Fact]
    public void Dtd_is_not_processed()
    {
        const string evil = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><MAP-msg-listing><msg handle=\"1\" subject=\"&e;\"/></MAP-msg-listing>";
        var list = MapXml.ParseMessagesListing(evil);
        Assert.DoesNotContain(list, m => (m.Subject ?? "").Contains("fonts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Folder_listing_parses_names()
    {
        var f = MapXml.ParseFolderListing("<folder-listing version=\"1.0\"><folder name=\"inbox\"/><folder name=\"outbox\" created=\"x\"/></folder-listing>");
        Assert.Equal(["inbox", "outbox"], f.Select(x => x.Name));
    }

    [Fact]
    public void Event_report_10_and_11_parse()
    {
        var v10 = MapXml.ParseEventReport("<MAP-event-report version=\"1.0\"><event type=\"NewMessage\" handle=\"12345678\" folder=\"TELECOM/MSG/INBOX\" msg_type=\"SMS_GSM\"/></MAP-event-report>");
        Assert.Equal(new MapEvent("NewMessage", "12345678", "TELECOM/MSG/INBOX", "SMS_GSM"), v10.Single());

        var v11 = MapXml.ParseEventReport("<MAP-event-report version=\"1.1\"><event type=\"NewMessage\" handle=\"1\" folder=\"telecom/msg/inbox\" msg_type=\"SMS_GSM\" datetime=\"20261106T101010\" subject=\"Coucou\" sender_name=\"Léa\" priority=\"no\"/><event type=\"MessageDeleted\" handle=\"2\"/></MAP-event-report>");
        Assert.Equal(2, v11.Count);
        Assert.Equal("Coucou", v11[0].Subject);
        Assert.Equal("Léa", v11[0].SenderName);
        Assert.Equal("MessageDeleted", v11[1].Type);
    }

    [Theory]
    [InlineData("20261106T101010Z", 0)]
    [InlineData("20261106T101010-0500", -5)]
    public void Map_time_offsets(string value, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), MapXml.ParseMapTime(value)!.Value.Offset);

    [Fact]
    public void Map_time_garbage_is_null() => Assert.Null(MapXml.ParseMapTime("yesterday"));
}

public class BMessageTests
{
    [Fact]
    public void Push_body_has_spec_length_and_recipient_in_benv()
    {
        var text = Encoding.UTF8.GetString(BMessage.BuildForPush("+33 6 12 34 56 78", "Héllo\nça va ?"));
        Assert.StartsWith("BEGIN:BMSG\r\nVERSION:1.0\r\n", text);
        Assert.Contains("TYPE:SMS_GSM\r\n", text);
        Assert.EndsWith("END:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n", text);
        int benv = text.IndexOf("BEGIN:BENV", StringComparison.Ordinal);
        Assert.True(text.IndexOf("TEL:+33612345678", StringComparison.Ordinal) > benv);

        var msgStart = text.IndexOf("BEGIN:MSG", StringComparison.Ordinal);
        var msgEnd = text.IndexOf("END:MSG\r\n", StringComparison.Ordinal) + "END:MSG\r\n".Length;
        var declared = int.Parse(text.Split("LENGTH:")[1].Split("\r\n")[0]);
        Assert.Equal(Encoding.UTF8.GetByteCount(text[msgStart..msgEnd]), declared);
        Assert.Contains("Héllo\r\nça va ?", text);
    }

    [Fact]
    public void Email_recipient_uses_EMAIL_property()
    {
        var text = Encoding.UTF8.GetString(BMessage.BuildForPush("arthur@example.com", "x"));
        Assert.Contains("EMAIL:arthur@example.com\r\n", text);
    }

    [Theory]
    [InlineData("06 12\r\nEND:VCARD")]
    [InlineData("a;b@c.d")]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("+")]
    public void Unsafe_recipients_are_rejected(string r) =>
        Assert.Throws<ArgumentException>(() => BMessage.BuildForPush(r, "x"));

    [Fact]
    public void End_marker_inside_text_survives_roundtrip()
    {
        var bytes = BMessage.BuildForPush("0612345678", "line1\nEND:MSG\nline3");
        var parsed = BMessage.Parse(Encoding.UTF8.GetString(bytes));
        Assert.Equal("line1\nEND:MSG\nline3", parsed.Body);
        Assert.Equal("0612345678", parsed.Recipients.Single().Phones.Single().Number);
        Assert.Equal("", parsed.Originator!.DisplayName);
    }

    [Fact]
    public void Parses_incoming_bmessage()
    {
        var m = BMessage.Parse(FakeIphoneMas.BMessageText(new FakeMessage("1", "Léa Martin", "+33600000000", "Coucou\nça va", "", false)));
        Assert.Equal("UNREAD", m.Status);
        Assert.Equal("SMS_GSM", m.Type);
        Assert.Equal("telecom/msg/inbox", m.Folder);
        Assert.Equal("Léa Martin", m.Originator!.DisplayName);
        Assert.Equal("+33600000000", m.Originator.Phones[0].Number);
        Assert.Equal("Coucou\nça va", m.Body);
    }
}

public class MapClientTests
{
    private static async Task<(MapClient Client, FakeIphoneMas Phone, Task Run, Loopback Link)> StartAsync(Action<FakeIphoneMas>? setup = null)
    {
        var link = await Loopback.CreateAsync();
        var phone = new FakeIphoneMas();
        setup?.Invoke(phone);
        var run = phone.RunAsync(link.Server);
        var client = new MapClient(_ => Task.FromResult(link.Client));
        await client.ConnectAsync();
        return (client, phone, run, link);
    }

    private static void Seed(FakeIphoneMas p, int count)
    {
        for (int i = 0; i < count; i++)
            p.Inbox.Add(new FakeMessage($"2000{i:D4}", $"Contact {i}", $"+3360000{i:D4}", $"Message {i}", "20261106T1000" + (i % 60).ToString("D2"), false));
    }

    [Fact]
    public async Task Connect_sends_features_and_lists_10_messages()
    {
        var (client, phone, _, link) = await StartAsync(p => Seed(p, 25));
        await using var _l = link;
        Assert.Equal(BluetoothConnectionState.Connected, client.State);
        Assert.Equal((ulong)MapFeatures.PhoneBridgeClient, phone.LastConnectParameters!.GetNumber(MapTag.MapSupportedFeatures));

        var list = await client.GetMessagesListingAsync("inbox", 10);
        Assert.Equal(10, list.Count);
        Assert.Equal("Message 0", list[0].Subject);
        Assert.Equal(25ul, client.LastListingParameters!.GetNumber(MapTag.ListingSize));
        Assert.Equal("telecom/msg", phone.CurrentPath);
        Assert.Equal(0, phone.OperationsWithoutConnectionId);
        Assert.All(phone.Inbox, m => Assert.False(m.Read)); // listing must not mark read
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Folder_listing_navigates_one_level_at_a_time()
    {
        var (client, _, _, link) = await StartAsync();
        await using var _l = link;
        var folders = await client.GetFolderListingAsync("telecom/msg");
        Assert.Contains(folders, f => f.Name == "inbox");
        Assert.Contains(folders, f => f.Name == "outbox");
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Push_long_message_goes_to_outbox_in_chunks()
    {
        var (client, phone, _, link) = await StartAsync(p => p.MaxPacketLength = 300);
        await using var _l = link;
        var text = string.Concat(Enumerable.Repeat("Bonjour 👋 ", 100));
        var handle = await client.PushMessageAsync("+33612345678", text);
        Assert.Equal("40000001", handle);
        var body = phone.PushedBodies.Single();
        Assert.Equal(text, BMessage.Parse(body).Body);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Get_message_status_and_registration()
    {
        var (client, phone, _, link) = await StartAsync(p => Seed(p, 2));
        await using var _l = link;
        var m = await client.GetMessageAsync("20000001");
        Assert.Equal("Message 1", m.Body);
        Assert.True(phone.Inbox[1].Read);

        await client.SetReadStatusAsync("20000001", false);
        Assert.False(phone.Inbox[1].Read);

        await client.SetNotificationRegistrationAsync(true);
        Assert.True(phone.NotificationsOn);
        await Assert.ThrowsAsync<ObexException>(() => client.SetReadStatusAsync("nope", true));
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Forbidden_connect_means_toggle_off()
    {
        await using var link = await Loopback.CreateAsync();
        var phone = new FakeIphoneMas { ConnectResult = ObexResponseCode.Forbidden };
        _ = phone.RunAsync(link.Server);
        var client = new MapClient(_ => Task.FromResult(link.Client));
        var e = await Assert.ThrowsAsync<ObexException>(() => client.ConnectAsync());
        Assert.Equal(ObexResponseCode.Forbidden, e.Code);
        Assert.Equal(BluetoothConnectionState.PermissionDenied, client.State);
    }

    [Fact]
    public async Task Mns_new_message_event_becomes_MessageReceived_without_marking_read()
    {
        var (client, phone, _, link) = await StartAsync(p => Seed(p, 3));
        await using var _l = link;
        var mns = new MnsServer();
        client.AttachMns(mns);
        var received = new TaskCompletionSource<MapMessage>();
        client.MessageReceived += (_, m) => received.TrySetResult(m);

        // The fake iPhone opens its MNS connection to us and pushes an event report.
        await using var mnsLink = await Loopback.CreateAsync();
        var serve = mns.RunConnectionAsync(mnsLink.Server);
        var iphone = new ObexClient(mnsLink.Client);
        Assert.True((await iphone.ConnectAsync(BluetoothIds.MnsTarget)).IsSuccess);
        var report = "<MAP-event-report version=\"1.0\"><event type=\"NewMessage\" handle=\"20000002\" folder=\"TELECOM/MSG/INBOX\" msg_type=\"SMS_GSM\"/></MAP-event-report>";
        var put = await iphone.PutAsync([ObexHeader.Type(MapTypes.EventReport), new AppParameters().AddByte(MapTag.MasInstanceId, 0).ToHeader()], Encoding.UTF8.GetBytes(report));
        Assert.True(put.IsSuccess);

        var m = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("20000002", m.Handle);
        Assert.Equal("Message 2", m.Body);
        Assert.Equal("Contact 2", m.SenderName);
        Assert.Equal(0, phone.GetMessageCalls);
        Assert.False(phone.Inbox[2].Read);

        await iphone.DisconnectAsync();
        await serve.WaitAsync(TimeSpan.FromSeconds(5));
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Mns_refuses_wrong_target()
    {
        await using var link = await Loopback.CreateAsync();
        var mns = new MnsServer();
        _ = mns.RunConnectionAsync(link.Server);
        var c = new ObexClient(link.Client);
        Assert.Equal(ObexResponseCode.NotAcceptable, (await c.ConnectAsync(BluetoothIds.MasTarget)).Code);
    }
}
