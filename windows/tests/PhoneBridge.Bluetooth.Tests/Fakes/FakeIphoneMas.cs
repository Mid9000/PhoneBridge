using System.Security;
using System.Text;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Obex;

namespace PhoneBridge.Bluetooth.Tests.Fakes;

public sealed record FakeMessage(string Handle, string Sender, string Address, string Text, string DateTime, bool Read);

/// <summary>
/// Simulated iPhone MAS (MSE): folder tree, inbox listing, GetMessage, PushMessage, status, notification registration.
/// Behaviour mirrors what BlueFerry/iphonebridge report for iOS (inbox only, text in subject, SMS_GSM for everything).
/// </summary>
public sealed class FakeIphoneMas
{
    private static readonly Dictionary<string, string[]> Tree = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = ["telecom"],
        ["telecom"] = ["msg"],
        ["telecom/msg"] = ["inbox", "outbox", "sent", "deleted"],
        ["telecom/msg/inbox"] = [],
        ["telecom/msg/outbox"] = [],
        ["telecom/msg/sent"] = [],
        ["telecom/msg/deleted"] = [],
    };

    private readonly List<string> _path = [];
    public List<FakeMessage> Inbox { get; } = [];
    public List<string> PushedBodies { get; } = [];
    public bool NotificationsOn { get; private set; }
    public ObexResponseCode ConnectResult { get; set; } = ObexResponseCode.Success;
    public ushort MaxPacketLength { get; set; } = 0xFFFE;
    public int GetMessageCalls { get; private set; }
    public int OperationsWithoutConnectionId { get; private set; }
    public AppParameters? LastConnectParameters { get; private set; }
    public const uint ConnectionId = 0x00001234;

    public string CurrentPath => string.Join('/', _path);

    public Task RunAsync(Stream stream, CancellationToken ct = default) =>
        new ObexServer(stream, HandleAsync) { LocalMaxPacketLength = MaxPacketLength }.RunAsync(ct);

    private Task<ObexServerReply> HandleAsync(ObexServerRequest r, CancellationToken ct)
    {
        if (r.Opcode != ObexOpcode.Connect && r.Headers.FirstOrDefault()?.Id != ObexHeaderId.ConnectionId) OperationsWithoutConnectionId++;
        return Task.FromResult(r.Opcode switch
        {
            ObexOpcode.Connect => Connect(r),
            ObexOpcode.SetPath => SetPath(r),
            ObexOpcode.Get => Get(r),
            ObexOpcode.Put => Put(r),
            ObexOpcode.Disconnect => ObexServerReply.Ok(),
            _ => ObexServerReply.Error(ObexResponseCode.NotImplemented),
        });
    }

    private ObexServerReply Connect(ObexServerRequest r)
    {
        if (ConnectResult != ObexResponseCode.Success) return ObexServerReply.Error(ConnectResult);
        var target = r.Headers.GetBytes(ObexHeaderId.Target);
        if (target is null || !target.AsSpan().SequenceEqual(BluetoothIds.MasTarget)) return ObexServerReply.Error(ObexResponseCode.ServiceUnavailable);
        LastConnectParameters = AppParameters.From(r.Headers);
        return ObexServerReply.Ok(ObexHeader.ConnectionId(ConnectionId), ObexHeader.Bytes(ObexHeaderId.Who, BluetoothIds.MasTarget));
    }

    private ObexServerReply SetPath(ObexServerRequest r)
    {
        byte flags = r.Prefix[0];
        if ((flags & 0x01) != 0)
        {
            if (_path.Count == 0) return ObexServerReply.Error(ObexResponseCode.NotFound);
            _path.RemoveAt(_path.Count - 1);
            return ObexServerReply.Ok();
        }
        var name = r.Headers.GetText(ObexHeaderId.Name) ?? "";
        if (name.Length == 0) { _path.Clear(); return ObexServerReply.Ok(); }
        if (!Tree[CurrentPath].Contains(name, StringComparer.OrdinalIgnoreCase)) return ObexServerReply.Error(ObexResponseCode.NotFound);
        _path.Add(name);
        return ObexServerReply.Ok();
    }

    private ObexServerReply Get(ObexServerRequest r)
    {
        var type = r.Headers.GetText(ObexHeaderId.Type);
        var name = r.Headers.GetText(ObexHeaderId.Name) ?? "";
        var p = AppParameters.From(r.Headers) ?? new AppParameters();
        switch (type)
        {
            case MapTypes.FolderListing:
                var xml = "<?xml version=\"1.0\"?>\n<folder-listing version=\"1.0\">" +
                          string.Concat(Tree[CurrentPath].Select(f => $"<folder name=\"{f}\"/>")) + "</folder-listing>";
                return Body(xml);

            case MapTypes.MessagesListing:
                var folder = string.Join('/', _path.Append(name).Where(s => s.Length > 0));
                if (!folder.Equals("telecom/msg/inbox", StringComparison.OrdinalIgnoreCase))
                    return folder.StartsWith("telecom/msg/", StringComparison.OrdinalIgnoreCase) ? Body("<MAP-msg-listing version=\"1.0\"></MAP-msg-listing>") : ObexServerReply.Error(ObexResponseCode.NotFound);
                int max = (int)(p.GetNumber(MapTag.MaxListCount) ?? 1024);
                int subjLen = (int)(p.GetNumber(MapTag.SubjectLength) ?? 255);
                var sb = new StringBuilder("<MAP-msg-listing version=\"1.0\">\n");
                foreach (var m in Inbox.Take(max))
                {
                    var subject = m.Text.Length > subjLen ? m.Text[..subjLen] : m.Text;
                    sb.Append($"<msg handle=\"{m.Handle}\" subject=\"{SecurityElement.Escape(subject)}\" datetime=\"{m.DateTime}\" " +
                              $"sender_name=\"{SecurityElement.Escape(m.Sender)}\" sender_addressing=\"{m.Address}\" type=\"SMS_GSM\" " +
                              $"size=\"{Encoding.UTF8.GetByteCount(m.Text)}\" read=\"{(m.Read ? "yes" : "no")}\" sent=\"no\" protected=\"no\"/>\n");
                }
                sb.Append("</MAP-msg-listing>");
                var reply = new AppParameters().AddUInt16(MapTag.ListingSize, (ushort)Inbox.Count).AddByte(MapTag.NewMessage, 1);
                return new ObexServerReply { Headers = new([reply.ToHeader()]), Body = Encoding.UTF8.GetBytes(sb.ToString()) };

            case MapTypes.Message:
                if (p.Get(MapTag.Charset) is null || p.Get(MapTag.Attachment) is null) return ObexServerReply.Error(ObexResponseCode.BadRequest);
                var msg = Inbox.FirstOrDefault(m => m.Handle == name);
                if (msg is null) return ObexServerReply.Error(ObexResponseCode.NotFound);
                GetMessageCalls++;
                Inbox[Inbox.IndexOf(msg)] = msg with { Read = true }; // MAP: download marks read
                return Body(BMessageText(msg));

            default:
                return ObexServerReply.Error(ObexResponseCode.BadRequest);
        }
    }

    private ObexServerReply Put(ObexServerRequest r)
    {
        var type = r.Headers.GetText(ObexHeaderId.Type);
        var name = r.Headers.GetText(ObexHeaderId.Name) ?? "";
        var p = AppParameters.From(r.Headers) ?? new AppParameters();
        switch (type)
        {
            case MapTypes.Message:
                if (CurrentPath != "telecom/msg" || name != "outbox" || p.Get(MapTag.Charset) is null || r.Body is null)
                    return ObexServerReply.Error(ObexResponseCode.BadRequest);
                PushedBodies.Add(Encoding.UTF8.GetString(r.Body));
                return ObexServerReply.Ok(ObexHeader.Name($"40000{PushedBodies.Count:D3}"));

            case MapTypes.MessageStatus:
                var m = Inbox.FirstOrDefault(x => x.Handle == name);
                if (m is null) return ObexServerReply.Error(ObexResponseCode.NotFound);
                if (p.GetNumber(MapTag.StatusIndicator) == 0) Inbox[Inbox.IndexOf(m)] = m with { Read = p.GetNumber(MapTag.StatusValue) == 1 };
                return ObexServerReply.Ok();

            case MapTypes.NotificationRegistration:
                NotificationsOn = p.GetNumber(MapTag.NotificationStatus) == 1;
                return ObexServerReply.Ok();

            default:
                return ObexServerReply.Error(ObexResponseCode.BadRequest);
        }
    }

    private static ObexServerReply Body(string text) => new() { Body = Encoding.UTF8.GetBytes(text) };

    public static string BMessageText(FakeMessage m)
    {
        var msg = "BEGIN:MSG\r\n" + m.Text + "\r\nEND:MSG\r\n";
        return "BEGIN:BMSG\r\nVERSION:1.0\r\nSTATUS:UNREAD\r\nTYPE:SMS_GSM\r\nFOLDER:telecom/msg/inbox\r\n" +
               $"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:{m.Sender}\r\nN:;{m.Sender};;;\r\nTEL:{m.Address}\r\nEND:VCARD\r\n" +
               "BEGIN:BENV\r\nBEGIN:BBODY\r\nCHARSET:UTF-8\r\n" +
               $"LENGTH:{Encoding.UTF8.GetByteCount(msg)}\r\n{msg}END:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n";
    }
}
