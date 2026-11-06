using System.Buffers.Binary;
using System.Text;
using PhoneBridge.Bluetooth.Ancs;

namespace PhoneBridge.Bluetooth.Tests.Fakes;

public sealed record FakeNotification(uint Uid, AncsCategory Category, string AppId, string Title, string Subtitle, string Message,
    string Date = "20261106T081500", AncsEventFlags Flags = AncsEventFlags.None, string Positive = "", string Negative = "");

/// <summary>Simulated iPhone ANCS server: answers Control Point commands with Data Source fragments of FragmentSize bytes.</summary>
public sealed class FakeAncsIphone : IAncsTransport
{
    private readonly Dictionary<uint, FakeNotification> _notifications = new();
    public Dictionary<string, string> AppNames { get; } = new() { ["com.apple.MobileSMS"] = "Messages", ["net.whatsapp.WhatsApp"] = "WhatsApp" };
    public bool Bonded { get; set; } = true;
    /// <summary>ATT MTU 23 leaves 20 bytes per notification: the worst case for reassembly.</summary>
    public int FragmentSize { get; set; } = 20;
    public List<(uint Uid, AncsAction Action)> Actions { get; } = [];
    public int AppAttributeRequests { get; private set; }

    public event EventHandler<byte[]>? NotificationSourceReceived;
    public event EventHandler<byte[]>? DataSourceReceived;
    public event EventHandler<bool>? ConnectionChanged;

    public Task SubscribeAsync(CancellationToken ct)
    {
        if (!Bonded) throw new AncsAccessDeniedException("not bonded");
        foreach (var n in _notifications.Values) RaiseSource(AncsEventId.Added, n with { Flags = n.Flags | AncsEventFlags.PreExisting });
        return Task.CompletedTask;
    }

    public void AddExisting(FakeNotification n) => _notifications[n.Uid] = n;

    public void Post(FakeNotification n)
    {
        _notifications[n.Uid] = n;
        RaiseSource(AncsEventId.Added, n);
    }

    public void Remove(uint uid)
    {
        if (_notifications.Remove(uid, out var n)) RaiseSource(AncsEventId.Removed, n);
    }

    public void Disconnect() => ConnectionChanged?.Invoke(this, false);

    private void RaiseSource(AncsEventId ev, FakeNotification n)
    {
        var b = new byte[8];
        b[0] = (byte)ev;
        b[1] = (byte)n.Flags;
        b[2] = (byte)n.Category;
        b[3] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), n.Uid);
        NotificationSourceReceived?.Invoke(this, b);
    }

    public Task WriteControlPointAsync(byte[] command, CancellationToken ct)
    {
        var response = new List<byte> { command[0] };
        switch ((AncsCommandId)command[0])
        {
            case AncsCommandId.GetNotificationAttributes:
            {
                uint uid = BinaryPrimitives.ReadUInt32LittleEndian(command.AsSpan(1));
                if (!_notifications.TryGetValue(uid, out var n)) throw new AncsControlPointException(AncsErrorCode.InvalidParameter, "unknown uid");
                response.AddRange(command.AsSpan(1, 4).ToArray());
                int i = 5;
                while (i < command.Length)
                {
                    var id = (AncsNotificationAttribute)command[i++];
                    int max = int.MaxValue;
                    if (AncsCommands.NeedsMaxLength(id)) { max = BinaryPrimitives.ReadUInt16LittleEndian(command.AsSpan(i)); i += 2; }
                    string value = id switch
                    {
                        AncsNotificationAttribute.AppIdentifier => n.AppId,
                        AncsNotificationAttribute.Title => n.Title,
                        AncsNotificationAttribute.Subtitle => n.Subtitle,
                        AncsNotificationAttribute.Message => n.Message,
                        AncsNotificationAttribute.MessageSize => Encoding.UTF8.GetByteCount(n.Message).ToString(),
                        AncsNotificationAttribute.Date => n.Date,
                        AncsNotificationAttribute.PositiveActionLabel => n.Positive,
                        AncsNotificationAttribute.NegativeActionLabel => n.Negative,
                        _ => "",
                    };
                    AppendAttribute(response, (byte)id, Encoding.UTF8.GetBytes(value).Take(max).ToArray());
                }
                break;
            }
            case AncsCommandId.GetAppAttributes:
            {
                AppAttributeRequests++;
                int nul = Array.IndexOf(command, (byte)0, 1);
                var appId = Encoding.UTF8.GetString(command, 1, nul - 1);
                response.AddRange(command.AsSpan(1, nul).ToArray()); // AppIdentifier + NUL
                for (int i = nul + 1; i < command.Length; i++)
                    AppendAttribute(response, command[i], Encoding.UTF8.GetBytes(AppNames.GetValueOrDefault(appId, "")));
                break;
            }
            case AncsCommandId.PerformNotificationAction:
                Actions.Add((BinaryPrimitives.ReadUInt32LittleEndian(command.AsSpan(1)), (AncsAction)command[5]));
                return Task.CompletedTask; // no Data Source output
            default:
                throw new AncsControlPointException(AncsErrorCode.UnknownCommand, "unknown command");
        }

        // Like a real link, fragments may arrive before the write response.
        var bytes = response.ToArray();
        for (int off = 0; off < bytes.Length; off += FragmentSize)
            DataSourceReceived?.Invoke(this, bytes.AsSpan(off, Math.Min(FragmentSize, bytes.Length - off)).ToArray());
        return Task.CompletedTask;
    }

    private static void AppendAttribute(List<byte> b, byte id, byte[] value)
    {
        b.Add(id);
        b.Add((byte)value.Length);
        b.Add((byte)(value.Length >> 8));
        b.AddRange(value);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
