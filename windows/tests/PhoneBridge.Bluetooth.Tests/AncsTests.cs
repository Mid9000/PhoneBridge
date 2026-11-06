using PhoneBridge.Bluetooth.Ancs;
using PhoneBridge.Bluetooth.Tests.Fakes;

namespace PhoneBridge.Bluetooth.Tests;

public class AncsProtocolTests
{
    [Fact]
    public void NotificationSource_parses_spec_layout()
    {
        // Added, Important|PositiveAction|NegativeAction, IncomingCall, count 1, uid 0x01020304 (little-endian)
        var ev = AncsNotificationSourceEvent.Parse([0x00, 0x1A, 0x01, 0x01, 0x04, 0x03, 0x02, 0x01]);
        Assert.Equal(AncsEventId.Added, ev.EventId);
        Assert.Equal(AncsEventFlags.Important | AncsEventFlags.PositiveAction | AncsEventFlags.NegativeAction, ev.Flags);
        Assert.Equal(AncsCategory.IncomingCall, ev.Category);
        Assert.Equal(1, ev.CategoryCount);
        Assert.Equal(0x01020304u, ev.Uid);
    }

    [Fact]
    public void NotificationSource_rejects_short_packet() =>
        Assert.Throws<FormatException>(() => AncsNotificationSourceEvent.Parse([0, 0, 0]));

    [Fact]
    public void GetNotificationAttributes_puts_max_length_only_on_title_subtitle_message()
    {
        var cmd = AncsCommands.GetNotificationAttributes(7, AncsCommands.DefaultNotificationAttributes);
        byte[] expected =
        [
            0x00, 7, 0, 0, 0,
            0x00,              // AppIdentifier
            0x01, 128, 0,      // Title, 128
            0x02, 128, 0,      // Subtitle, 128
            0x03, 0x00, 0x04,  // Message, 1024 little-endian
            0x05, 0x06, 0x07,  // Date, PositiveActionLabel, NegativeActionLabel
        ];
        Assert.Equal(expected, cmd);
    }

    [Fact]
    public void GetAppAttributes_null_terminates_app_identifier()
    {
        var cmd = AncsCommands.GetAppAttributes("a.b", [new((byte)AncsAppAttribute.DisplayName)]);
        Assert.Equal(new byte[] { 0x01, (byte)'a', (byte)'.', (byte)'b', 0x00, 0x00 }, cmd);
    }

    [Fact]
    public void PerformNotificationAction_layout()
    {
        Assert.Equal(new byte[] { 0x02, 0x10, 0x00, 0x00, 0x00, 0x01 }, AncsCommands.PerformNotificationAction(16, AncsAction.Negative));
    }

    [Fact]
    public void Reassembler_handles_one_byte_fragments_and_empty_attributes()
    {
        byte[] response = [0x00, 9, 0, 0, 0, 0x00, 3, 0, (byte)'a', (byte)'p', (byte)'p', 0x01, 0, 0];
        var r = new AncsDataSourceReassembler();
        r.Expect(AncsCommandId.GetNotificationAttributes, 2);
        AncsAttributeResponse? result = null;
        for (int i = 0; i < response.Length; i++)
        {
            result = r.Append(response.AsSpan(i, 1));
            if (i < response.Length - 1) Assert.Null(result);
        }
        Assert.NotNull(result);
        Assert.Equal(9u, result!.Uid);
        Assert.Equal("app", result.Attributes[0]);
        Assert.Equal("", result.Attributes[1]);
        Assert.False(r.IsPending);
    }

    [Fact]
    public void Reassembler_parses_app_attributes_response()
    {
        byte[] response = [0x01, (byte)'x', 0, 0x00, 2, 0, (byte)'O', (byte)'K'];
        var r = new AncsDataSourceReassembler();
        r.Expect(AncsCommandId.GetAppAttributes, 1);
        var result = r.Append(response);
        Assert.Equal("x", result!.AppIdentifier);
        Assert.Equal("OK", result.Attributes[0]);
    }

    [Fact]
    public void Reassembler_ignores_unsolicited_and_mismatched_data()
    {
        var r = new AncsDataSourceReassembler();
        Assert.Null(r.Append([0x00, 1, 0, 0, 0]));
        r.Expect(AncsCommandId.GetAppAttributes, 1);
        Assert.Null(r.Append([0x00, 1, 0, 0, 0])); // tail of an older GetNotificationAttributes
        Assert.True(r.IsPending);
    }

    [Fact]
    public void Date_parses_ancs_format() =>
        Assert.Equal(new DateTime(2026, 11, 6, 8, 15, 0), AncsCommands.ParseDate("20261106T081500"));

    [Fact]
    public void Uuids_match_apple_spec()
    {
        Assert.Equal("7905f431-b5ce-4e99-a40f-4b1e122d00d0", BluetoothIds.AncsService.ToString());
        Assert.Equal("9fbf120d-6301-42d9-8c58-25e699a21dbd", BluetoothIds.AncsNotificationSource.ToString());
        Assert.Equal("69d1d8f3-45e1-49a8-9821-9bbdfdaad9d9", BluetoothIds.AncsControlPoint.ToString());
        Assert.Equal("22eac6e9-24d6-4bb5-be44-b36ace7c7bfb", BluetoothIds.AncsDataSource.ToString());
    }

    [Fact]
    public void Solicitation_payload_is_little_endian_uuid()
    {
        var p = Platform.AncsAdvertiser.SolicitationPayload();
        Assert.Equal(16, p.Length);
        Assert.Equal(0xD0, p[0]);  // last byte of ...122D00D0
        Assert.Equal(0x79, p[15]); // first byte of 7905F431...
    }
}

public class AncsSessionTests
{
    private static async Task<T> Wait<T>(TaskCompletionSource<T> tcs) => await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Fetches_attributes_and_app_name_through_20_byte_fragments()
    {
        var phone = new FakeAncsIphone();
        await using var session = new AncsSession(phone);
        var got = new TaskCompletionSource<AncsNotification>();
        session.NotificationReceived += (_, n) => got.TrySetResult(n);
        await session.StartAsync();
        Assert.Equal(BluetoothConnectionState.Connected, session.State);

        var longText = new string('x', 1500);
        phone.Post(new FakeNotification(42, AncsCategory.Social, "net.whatsapp.WhatsApp", "Maman", "", longText));
        var n = await Wait(got);

        Assert.Equal(42u, n.Uid);
        Assert.Equal("net.whatsapp.WhatsApp", n.AppIdentifier);
        Assert.Equal("WhatsApp", n.AppDisplayName);
        Assert.Equal("Maman", n.Title);
        Assert.Null(n.Subtitle);
        Assert.Equal(1024, n.Message!.Length); // truncated by our max length
        Assert.Equal(new DateTime(2026, 11, 6, 8, 15, 0), n.Date);
        Assert.False(n.IsPreExisting);
    }

    [Fact]
    public async Task Caches_app_names()
    {
        var phone = new FakeAncsIphone();
        await using var session = new AncsSession(phone);
        int count = 0;
        var done = new TaskCompletionSource<bool>();
        session.NotificationReceived += (_, _) => { if (Interlocked.Increment(ref count) == 3) done.TrySetResult(true); };
        await session.StartAsync();
        for (uint i = 1; i <= 3; i++) phone.Post(new FakeNotification(i, AncsCategory.Social, "com.apple.MobileSMS", "A", "", "b"));
        await Wait(done);
        Assert.Equal(1, phone.AppAttributeRequests);
    }

    [Fact]
    public async Task Incoming_call_raises_IncomingCall_and_actions_reach_the_phone()
    {
        var phone = new FakeAncsIphone();
        await using var session = new AncsSession(phone);
        var call = new TaskCompletionSource<AncsNotification>();
        session.IncomingCall += (_, n) => call.TrySetResult(n);
        await session.StartAsync();
        phone.Post(new FakeNotification(5, AncsCategory.IncomingCall, "com.apple.mobilephone", "Paul", "", "",
            Flags: AncsEventFlags.PositiveAction | AncsEventFlags.NegativeAction, Positive: "Accept", Negative: "Decline"));
        var n = await Wait(call);
        Assert.Equal("Decline", n.NegativeActionLabel);

        await session.PerformActionAsync(n.Uid, AncsAction.Negative);
        Assert.Equal((5u, AncsAction.Negative), phone.Actions.Single());
    }

    [Fact]
    public async Task Removal_and_preexisting_flags_are_reported()
    {
        var phone = new FakeAncsIphone();
        phone.AddExisting(new FakeNotification(1, AncsCategory.Email, "com.apple.mobilemail", "Old", "", ""));
        await using var session = new AncsSession(phone);
        var existing = new TaskCompletionSource<AncsNotification>();
        var removed = new TaskCompletionSource<uint>();
        session.NotificationReceived += (_, n) => existing.TrySetResult(n);
        session.NotificationRemoved += (_, uid) => removed.TrySetResult(uid);
        await session.StartAsync();
        Assert.True((await Wait(existing)).IsPreExisting);
        phone.Remove(1);
        Assert.Equal(1u, await Wait(removed));
    }

    [Fact]
    public async Task Unknown_uid_error_does_not_block_following_notifications()
    {
        var phone = new FakeAncsIphone();
        await using var session = new AncsSession(phone);
        var got = new TaskCompletionSource<AncsNotification>();
        session.NotificationReceived += (_, n) => { if (n.Uid == 11) got.TrySetResult(n); };
        await session.StartAsync();
        // A notification removed between Notification Source and our request -> 0xA2 from the phone.
        phone.Post(new FakeNotification(10, AncsCategory.Social, "a", "t", "", "m"));
        phone.Remove(10);
        phone.Post(new FakeNotification(11, AncsCategory.Social, "a", "t", "", "m"));
        Assert.Equal(11u, (await Wait(got)).Uid);
    }

    [Fact]
    public async Task Not_bonded_maps_to_PermissionDenied()
    {
        var phone = new FakeAncsIphone { Bonded = false };
        await using var session = new AncsSession(phone);
        await Assert.ThrowsAsync<AncsAccessDeniedException>(() => session.StartAsync());
        Assert.Equal(BluetoothConnectionState.PermissionDenied, session.State);
    }

    [Fact]
    public async Task Disconnect_is_reported()
    {
        var phone = new FakeAncsIphone();
        await using var session = new AncsSession(phone);
        await session.StartAsync();
        phone.Disconnect();
        Assert.Equal(BluetoothConnectionState.Disconnected, session.State);
    }
}
