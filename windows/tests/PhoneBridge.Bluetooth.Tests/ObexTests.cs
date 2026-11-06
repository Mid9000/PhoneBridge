using PhoneBridge.Bluetooth.Obex;
using PhoneBridge.Bluetooth.Tests.Fakes;

namespace PhoneBridge.Bluetooth.Tests;

public class ObexEncodingTests
{
    [Fact]
    public void Connect_request_matches_irobex_layout()
    {
        var p = new ObexPacket(ObexOpcode.Connect, ObexPacket.ConnectPrefix(0x2000), [ObexHeader.Bytes(ObexHeaderId.Target, BluetoothIds.MasTarget)]);
        var b = p.Encode();
        Assert.Equal(new byte[] { 0x80, 0x00, 0x1A, 0x10, 0x00, 0x20, 0x00, 0x46, 0x00, 0x13 }, b[..10]);
        Assert.Equal(BluetoothIds.MasTarget, b[10..]);
        Assert.Equal(0xBB, b[10]); // network byte order
    }

    [Fact]
    public void Text_header_is_utf16be_null_terminated_and_empty_name_is_3_bytes()
    {
        var output = new List<byte>();
        ObexHeader.Name("ab").WriteTo(output);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x09, 0x00, 0x61, 0x00, 0x62, 0x00, 0x00 }, output);

        output.Clear();
        ObexHeader.Name("").WriteTo(output);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x03 }, output);
    }

    [Fact]
    public void Type_header_is_ascii_null_terminated()
    {
        var output = new List<byte>();
        ObexHeader.Type("x-bt/message").WriteTo(output);
        Assert.Equal(3 + 12 + 1, output.Count);
        Assert.Equal(0x42, output[0]);
        Assert.Equal(0, output[^1]);
        Assert.Equal("x-bt/message", ObexHeaderList.Parse(output.ToArray()).GetText(ObexHeaderId.Type));
    }

    [Fact]
    public void Headers_roundtrip_all_four_encodings()
    {
        var headers = new ObexHeaderList([
            ObexHeader.ConnectionId(0xDEADBEEF),
            ObexHeader.Name("telecom"),
            ObexHeader.Byte(0x93, 7),
            ObexHeader.Bytes(ObexHeaderId.Body, [1, 2, 3]),
        ]);
        var raw = new ObexPacket(0xA0, null, headers).Encode();
        var parsed = ObexPacket.Decode(raw, 0);
        Assert.Equal(ObexResponseCode.Success, parsed.ResponseCode);
        Assert.True(parsed.IsFinal);
        Assert.Equal(0xDEADBEEFu, parsed.Headers.GetUInt32(ObexHeaderId.ConnectionId));
        Assert.Equal("telecom", parsed.Headers.GetText(ObexHeaderId.Name));
        Assert.Equal(new byte[] { 7 }, parsed.Headers.GetBytes(0x93));
        Assert.Equal(new byte[] { 1, 2, 3 }, parsed.Headers.GetBytes(ObexHeaderId.Body));
    }

    [Theory]
    [InlineData(new byte[] { 0xA0, 0x00, 0x05, 0x48, 0x00 })]                    // header length cut
    [InlineData(new byte[] { 0xA0, 0x00, 0x06, 0x48, 0x00, 0x09 })]              // header overruns packet
    [InlineData(new byte[] { 0xA0, 0x00, 0x05, 0xCB, 0x00 })]                    // 4-byte header cut
    [InlineData(new byte[] { 0xA0, 0x00, 0x09, 0xCB, 0x00 })]                    // length field lies
    public void Malformed_packets_throw_protocol_exception(byte[] raw) =>
        Assert.Throws<ObexProtocolException>(() => ObexPacket.Decode(raw, 0));

    [Fact]
    public void AppParameters_roundtrip()
    {
        var p = new AppParameters().AddUInt16(0x01, 10).AddByte(0x13, 255).AddUInt32(0x29, 0x0000005F);
        var parsed = AppParameters.Parse(p.Encode());
        Assert.Equal(new byte[] { 0x01, 2, 0, 10, 0x13, 1, 255, 0x29, 4, 0, 0, 0, 0x5F }, p.Encode());
        Assert.Equal(10ul, parsed.GetNumber(0x01));
        Assert.Equal(255ul, parsed.GetNumber(0x13));
        Assert.Equal(0x5Ful, parsed.GetNumber(0x29));
        Assert.Null(parsed.GetNumber(0x02));
        Assert.Throws<ObexProtocolException>(() => AppParameters.Parse([0x01, 5, 0]));
    }
}

public class ObexClientServerTests
{
    [Fact]
    public async Task Get_spanning_many_packets_is_reassembled_and_continuations_carry_no_headers()
    {
        await using var link = await Loopback.CreateAsync();
        var body = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        var seen = new List<ObexServerRequest>();
        var server = new ObexServer(link.Server, (r, _) =>
        {
            seen.Add(r);
            return Task.FromResult(r.Opcode == ObexOpcode.Get ? new ObexServerReply { Body = body } : ObexServerReply.Ok(ObexHeader.ConnectionId(9)));
        });
        var run = server.RunAsync();

        var client = new ObexClient(link.Client, localMaxPacketLength: 300);
        Assert.True((await client.ConnectAsync(BluetoothIds.PbapTarget)).IsSuccess);
        var r = await client.GetAsync([ObexHeader.Type("x-test")]);

        Assert.True(r.IsSuccess);
        Assert.Equal(body, r.Body);
        Assert.Equal(2, seen.Count); // the server handler sees one Connect and one logical GET
        Assert.Equal(ObexHeaderId.ConnectionId, seen[1].Headers[0].Id);

        await client.DisconnectAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Put_is_split_to_peer_max_and_reassembled()
    {
        await using var link = await Loopback.CreateAsync();
        var body = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray();
        ObexServerRequest? put = null;
        var server = new ObexServer(link.Server, (r, _) =>
        {
            if (r.Opcode == ObexOpcode.Put) put = r;
            return Task.FromResult(ObexServerReply.Ok());
        }) { LocalMaxPacketLength = 255 };
        var run = server.RunAsync();

        var client = new ObexClient(link.Client);
        var c = await client.ConnectAsync(null);
        Assert.Equal(255, c.MaxPacketLength);
        Assert.Equal(255, client.PeerMaxPacketLength);
        var r = await client.PutAsync([ObexHeader.Name("outbox"), ObexHeader.Type("x-bt/message")], body);

        Assert.True(r.IsSuccess);
        Assert.Equal(body, put!.Body);
        Assert.Equal("outbox", put.Headers.GetText(ObexHeaderId.Name));
        await client.DisposeAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Forbidden_connect_is_reported_with_code()
    {
        await using var link = await Loopback.CreateAsync();
        var run = new ObexServer(link.Server, (_, _) => Task.FromResult(ObexServerReply.Error(ObexResponseCode.Forbidden))).RunAsync();
        var client = new ObexClient(link.Client);
        var r = await client.ConnectAsync(BluetoothIds.MasTarget);
        Assert.Equal(ObexResponseCode.Forbidden, r.Code);
        Assert.Null(client.ConnectionId);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Silent_peer_times_out()
    {
        await using var link = await Loopback.CreateAsync();
        var client = new ObexClient(link.Client) { ResponseTimeout = TimeSpan.FromMilliseconds(200) };
        await Assert.ThrowsAsync<TimeoutException>(() => client.ConnectAsync(BluetoothIds.MasTarget));
    }

    [Fact]
    public async Task SetPath_flags_root_child_up()
    {
        await using var link = await Loopback.CreateAsync();
        var prefixes = new List<(byte Flags, string? Name)>();
        var run = new ObexServer(link.Server, (r, _) =>
        {
            if (r.Opcode == ObexOpcode.SetPath) prefixes.Add((r.Prefix[0], r.Headers.Find(ObexHeaderId.Name)?.AsText()));
            return Task.FromResult(ObexServerReply.Ok());
        }).RunAsync();
        var client = new ObexClient(link.Client);
        await client.ConnectAsync(null);
        await client.SetPathAsync("");
        await client.SetPathAsync("telecom");
        await client.SetPathAsync("", up: true);
        Assert.Equal([(0x02, ""), (0x02, "telecom"), (0x03, null)], prefixes);
        await client.DisposeAsync();
    }
}
