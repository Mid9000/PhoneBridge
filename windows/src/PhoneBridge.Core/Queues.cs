namespace PhoneBridge.Core;

/// <summary>Items waiting for the iPhone (files, clipboard, links, relayed replies). Persisted in SQLite.</summary>
public sealed class OutboxQueue(Database db, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Raised with the pending count whenever the queue changes.</summary>
    public event Action<int>? Changed;

    /// <summary>Raised when the iPhone acknowledges (DELETE) an item.</summary>
    public event Action<OutboxItem>? Delivered;

    public IReadOnlyList<OutboxItem> Pending() => db.ListPendingOutbox();
    public OutboxItem? Get(string id) => db.GetOutbox(id);

    public OutboxItem EnqueueFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("File not found", path);
        var transfer = db.AddTransfer(new TransferRecord(0, "toPhone", info.Name, info.Length, null, info.FullName, "queued", Now));
        return Add(NewItem(OutboxKinds.File) with { Name = info.Name, Size = info.Length, Path = info.FullName, TransferId = transfer.Id });
    }

    public OutboxItem EnqueueClipboard(string text) => Add(NewItem(OutboxKinds.Clipboard) with { Text = text });
    public OutboxItem EnqueueLink(string url) => Add(NewItem(OutboxKinds.Link) with { Url = url });
    public OutboxItem EnqueueReply(string to, string body, long messageId) =>
        Add(NewItem(OutboxKinds.Reply) with { To = to, Text = body, MessageId = messageId });

    public bool Acknowledge(string id)
    {
        var item = db.GetOutbox(id);
        if (item is null || !db.MarkOutboxDelivered(id, Now)) return false;
        if (item.TransferId is long t) db.UpdateTransferStatus(t, "done");
        Delivered?.Invoke(item);
        Changed?.Invoke(db.ListPendingOutbox().Count);
        return true;
    }

    DateTimeOffset Now => _time.GetUtcNow();
    OutboxItem NewItem(string kind) => new(Guid.NewGuid().ToString("N"), kind, null, null, null, null, null, Now);

    OutboxItem Add(OutboxItem item)
    {
        db.AddOutbox(item);
        Changed?.Invoke(db.ListPendingOutbox().Count);
        return item;
    }
}

/// <summary>A way to send a text message through the iPhone (e.g. Bluetooth MAP in phase 3).</summary>
public interface IReplyChannel
{
    string Name { get; }
    /// <returns>true if the message was handed to the phone; false if the channel is unavailable.</returns>
    Task<bool> TrySendAsync(string to, string body, CancellationToken ct);
}

/// <summary>
/// Sends a reply through the most automatic channel available (SPEC §6), else queues it in the outbox for the
/// relay Shortcut. The stored status is always honest: "sent" only when a channel or the relay took it.
/// </summary>
public sealed class ReplyQueue
{
    readonly Database _db;
    readonly OutboxQueue _outbox;
    readonly IReadOnlyList<IReplyChannel> _channels;
    readonly TimeProvider _time;

    public ReplyQueue(Database db, OutboxQueue outbox, IEnumerable<IReplyChannel>? channels = null, TimeProvider? time = null)
    {
        (_db, _outbox, _channels, _time) = (db, outbox, (channels ?? []).ToList(), time ?? TimeProvider.System);
        _outbox.Delivered += item =>
        {
            if (item.Kind != OutboxKinds.Reply || item.MessageId is not long id) return;
            _db.UpdateMessageStatus(id, SendStatus.Sent, "relay");
            StatusChanged?.Invoke(_db.GetMessage(id)!);
        };
    }

    public event Action<MessageRecord>? StatusChanged;

    public async Task<MessageRecord> SendAsync(string to, string body, string service = "imessage", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Recipient and body are required.");
        var msg = _db.AddMessage(new MessageRecord(0, to, body, service, "out", _time.GetUtcNow(), SendStatus.Sending));

        foreach (var channel in _channels)
        {
            bool ok;
            try { ok = await channel.TrySendAsync(to, body, ct); }
            catch (OperationCanceledException) { throw; }
            catch { ok = false; }   // a broken channel must not lose the reply: fall through to the next one
            if (!ok) continue;
            _db.UpdateMessageStatus(msg.Id, SendStatus.Sent, channel.Name);
            return msg with { Status = SendStatus.Sent, Via = channel.Name };
        }

        _outbox.EnqueueReply(to, body, msg.Id);
        _db.UpdateMessageStatus(msg.Id, SendStatus.Queued, "relay");
        return msg with { Status = SendStatus.Queued, Via = "relay" };
    }
}
