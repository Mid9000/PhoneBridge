using System.Text;
using PhoneBridge.Bluetooth.Obex;
using PhoneBridge.Bluetooth.Pbap;
using PhoneBridge.Bluetooth.Platform;
using PhoneBridge.Bluetooth.Tests.Fakes;

namespace PhoneBridge.Bluetooth.Tests;

public class VCardTests
{
    private const string Phonebook =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:\r\nN:;;;;\r\nTEL;TYPE=CELL:+33600000000\r\nEND:VCARD\r\n" +
        "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Léa Martin\r\nN:Martin;Léa;;;\r\nTEL;TYPE=CELL,VOICE:+33 6 11 22 33 44\r\n" +
        "item1.TEL:01 23 45 67 89\r\nitem1.X-ABLabel:Maison\r\nEMAIL;TYPE=INTERNET:lea@example.com\r\nORG:ACME\\, Inc;R&D\r\n" +
        "PHOTO;ENCODING=b;TYPE=JPEG:/9j/\r\n 4AE=\r\nEND:VCARD\r\n" +
        "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Dupr=C3=A9;Ren=\r\n=C3=A9;;;\r\nTEL;CELL;VOICE:0699\r\nEND:VCARD\r\n";

    [Fact]
    public void Parses_names_phones_emails_org_photo()
    {
        var cards = VCard.ParseMany(Phonebook);
        Assert.Equal(3, cards.Count);
        Assert.Equal("+33600000000", cards[0].DisplayName); // owner card with no name falls back to number

        var lea = cards[1];
        Assert.Equal("Léa Martin", lea.DisplayName);
        Assert.Equal(["Martin", "Léa", "", "", ""], lea.NameParts);
        Assert.Equal(2, lea.Phones.Count);
        Assert.Equal(["CELL", "VOICE"], lea.Phones[0].Types);
        Assert.Equal("01 23 45 67 89", lea.Phones[1].Number);
        Assert.Equal("lea@example.com", lea.Emails.Single());
        Assert.Equal("ACME, Inc", lea.Organization);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01 }, lea.Photo);
    }

    [Fact]
    public void Parses_vcard21_quoted_printable_with_soft_break_and_bare_types()
    {
        var c = VCard.ParseMany(Phonebook)[2];
        Assert.Equal("René Dupré", c.DisplayName);
        Assert.Equal(["CELL", "VOICE"], c.Phones[0].Types);
    }

    [Fact]
    public void Parses_call_history_entry()
    {
        var c = VCard.ParseMany("BEGIN:VCARD\nVERSION:2.1\nN:;Paul\nTEL:+3361\nX-IRMC-CALL-DATETIME;MISSED:20261105T183000\nEND:VCARD").Single();
        Assert.Equal("MISSED", c.CallType);
        Assert.Equal(new DateTime(2026, 11, 5, 18, 30, 0), c.CallDateTime!.Value.DateTime);
        Assert.Equal("Paul", c.DisplayName);
    }

    [Fact]
    public void Garbage_does_not_throw()
    {
        Assert.Empty(VCard.ParseMany("random\nEND:VCARD\n:::"));
        var c = VCard.ParseMany("BEGIN:VCARD\nPHOTO;ENCODING=b:@@@\nFN:\\\nEND:VCARD").Single();
        Assert.Null(c.Photo);
    }
}

public class PbapClientTests
{
    [Fact]
    public async Task Connect_size_and_pull_against_fake_pse()
    {
        await using var link = await Loopback.CreateAsync();
        var vcards = string.Concat(Enumerable.Range(0, 200).Select(i => $"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Contact {i}\r\nTEL:+33600{i:D4}\r\nEND:VCARD\r\n"));
        AppParameters? lastPull = null;
        var run = new ObexServer(link.Server, (r, _) =>
        {
            if (r.Opcode == ObexOpcode.Connect)
                return Task.FromResult(r.Headers.GetBytes(ObexHeaderId.Target)!.AsSpan().SequenceEqual(BluetoothIds.PbapTarget)
                    ? ObexServerReply.Ok(ObexHeader.ConnectionId(1))
                    : ObexServerReply.Error(ObexResponseCode.ServiceUnavailable));
            if (r.Opcode != ObexOpcode.Get) return Task.FromResult(ObexServerReply.Ok());
            var p = AppParameters.From(r.Headers)!;
            if (r.Headers.GetText(ObexHeaderId.Name) != "telecom/pb.vcf") return Task.FromResult(ObexServerReply.Error(ObexResponseCode.NotFound));
            if (p.GetNumber(PbapTag.MaxListCount) == 0)
                return Task.FromResult(new ObexServerReply { Headers = new([new AppParameters().AddUInt16(PbapTag.PhonebookSize, 200).ToHeader()]), Body = [] });
            lastPull = p;
            return Task.FromResult(new ObexServerReply { Body = Encoding.UTF8.GetBytes(vcards) });
        }).RunAsync();

        var client = new PbapClient(_ => Task.FromResult(link.Client));
        await client.ConnectAsync();
        Assert.Equal(200, await client.GetPhonebookSizeAsync());
        var cards = await client.PullPhonebookAsync(offset: 1);
        Assert.Equal(200, cards.Count);
        Assert.Equal("Contact 199", cards[^1].DisplayName);
        Assert.Null(lastPull!.Get(PbapTag.MaxListCount)); // omitted on purpose (iOS quirk)
        Assert.Equal(1ul, lastPull.GetNumber(PbapTag.ListStartOffset));
        Assert.Equal(1ul, lastPull.GetNumber(PbapTag.Format));
        await client.DisposeAsync();
    }
}

public class SdpTests
{
    [Fact]
    public void Profile_descriptor_list_encoding()
    {
        Assert.Equal(new byte[] { 0x35, 0x08, 0x35, 0x06, 0x19, 0x11, 0x34, 0x09, 0x01, 0x04 }, Sdp.ProfileDescriptorList(0x1134, 0x0104));
    }

    [Fact]
    public void Parses_nested_elements_text_and_uint32()
    {
        var e = Sdp.Parse(Sdp.ProfileDescriptorList(0x1132, 0x0104));
        Assert.Equal(SdpElement.TypeSequence, e.Type);
        var inner = e.Items![0].Items!;
        Assert.Equal(0x1132ul, inner[0].Number);
        Assert.Equal(0x0104ul, inner[1].Number);

        Assert.Equal("MAP MAS-iOS", Sdp.Parse(Sdp.Text("MAP MAS-iOS")).Text);
        Assert.Equal(0x0000007Ful, Sdp.Parse(Sdp.UInt32(0x7F)).Number);
        Assert.Equal(0x0Aul, Sdp.Parse(Sdp.UInt8(0x0A)).Number);
    }

    [Fact]
    public void Truncated_element_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => Sdp.Parse([0x35, 0x08, 0x19]));
        Assert.Throws<FormatException>(() => Sdp.Parse([0x0A, 0x00]));
    }
}

public class IdsTests
{
    [Fact]
    public void Obex_targets_are_network_order()
    {
        Assert.Equal("BB582B40420C11DBB0DE0800200C9A66", Convert.ToHexString(BluetoothIds.MasTarget));
        Assert.Equal("BB582B41420C11DBB0DE0800200C9A66", Convert.ToHexString(BluetoothIds.MnsTarget));
        Assert.Equal("796135F0F0C511D809660800200C9A66", Convert.ToHexString(BluetoothIds.PbapTarget));
    }
}
