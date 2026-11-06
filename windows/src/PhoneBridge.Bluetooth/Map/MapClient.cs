using System.Text;
using PhoneBridge.Bluetooth.Obex;

namespace PhoneBridge.Bluetooth.Map;

/// <summary>MAP client (MCE) over an OBEX stream to the iPhone's MAS. Transport-agnostic: the stream comes from a delegate.</summary>
public sealed class MapClient : IMapClient
{
    private readonly Func<CancellationToken, Task<Stream>> _openStream;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _op = new(1, 1);
    private ObexClient? _obex;
    private string[] _path = [];
    private MnsServer? _mns;

    public BluetoothConnectionState State { get; private set; } = BluetoothConnectionState.Disconnected;
    public MapFeatures Features { get; init; } = MapFeatures.PhoneBridgeClient;
    /// <summary>Response app parameters from the last listing (ListingSize, NewMessage, MSETime) for diagnostics.</summary>
    public AppParameters? LastListingParameters { get; private set; }

    public event EventHandler<MapMessage>? MessageReceived;
    public event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;

    public MapClient(Func<CancellationToken, Task<Stream>> openStream, Action<string>? log = null)
    {
        _openStream = openStream;
        _log = log;
    }

    /// <summary>Feeds MNS NewMessage events into MessageReceived (fetches the message details over MAS).</summary>
    public void AttachMns(MnsServer mns)
    {
        _mns = mns;
        mns.EventReceived += OnMnsEvent;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        SetState(BluetoothConnectionState.Connecting);
        try
        {
            var stream = await _openStream(ct).ConfigureAwait(false);
            _obex = new ObexClient(stream, _log);
            var features = new AppParameters().AddUInt32(MapTag.MapSupportedFeatures, (uint)Features);
            var r = await _obex.ConnectAsync(BluetoothIds.MasTarget, [features.ToHeader()], ct).ConfigureAwait(false);
            if (!r.IsSuccess)
            {
                SetState(r.Code is ObexResponseCode.Forbidden or ObexResponseCode.Unauthorized ? BluetoothConnectionState.PermissionDenied : BluetoothConnectionState.Unavailable);
                throw new ObexException(r.Code, "MAP connect");
            }
            _path = [];
            SetState(BluetoothConnectionState.Connected);
        }
        catch (Exception) when (State == BluetoothConnectionState.Connecting)
        {
            SetState(BluetoothConnectionState.Unavailable);
            throw;
        }
    }

    public Task<IReadOnlyList<MapFolder>> GetFolderListingAsync(string path, CancellationToken ct = default) => Locked(async () =>
    {
        await NavigateAsync(path, ct).ConfigureAwait(false);
        var r = await Obex.GetAsync([ObexHeader.Type(MapTypes.FolderListing)], ct).ConfigureAwait(false);
        Ensure(r, "GetFolderListing");
        return MapXml.ParseFolderListing(Encoding.UTF8.GetString(r.Body));
    }, ct);

    /// <summary>folder is relative to telecom/msg (e.g. "inbox") or absolute ("telecom/msg/inbox").</summary>
    public Task<IReadOnlyList<MapMessageEntry>> GetMessagesListingAsync(string folder = "inbox", int maxCount = 10, int subjectLength = 255, CancellationToken ct = default) => Locked(async () =>
    {
        var (parent, name) = SplitFolder(folder);
        await NavigateAsync(parent, ct).ConfigureAwait(false);
        var p = new AppParameters()
            .AddUInt16(MapTag.MaxListCount, (ushort)Math.Clamp(maxCount, 0, 0xFFFF))
            .AddByte(MapTag.SubjectLength, (byte)Math.Clamp(subjectLength, 1, 255));
        var r = await Obex.GetAsync([ObexHeader.Name(name), ObexHeader.Type(MapTypes.MessagesListing), p.ToHeader()], ct).ConfigureAwait(false);
        Ensure(r, "GetMessagesListing");
        LastListingParameters = AppParameters.From(r.Headers);
        return MapXml.ParseMessagesListing(Encoding.UTF8.GetString(r.Body));
    }, ct);

    /// <summary>Note: per MAP, downloading a message may mark it read on the iPhone. Prefer the listing's subject when it suffices.</summary>
    public Task<BMessage> GetMessageAsync(string handle, CancellationToken ct = default) => Locked(async () =>
    {
        var p = new AppParameters().AddByte(MapTag.Attachment, 0).AddByte(MapTag.Charset, 1);
        var r = await Obex.GetAsync([ObexHeader.Name(handle), ObexHeader.Type(MapTypes.Message), p.ToHeader()], ct).ConfigureAwait(false);
        Ensure(r, "GetMessage");
        return BMessage.Parse(Encoding.UTF8.GetString(r.Body));
    }, ct);

    public Task<string?> PushMessageAsync(string recipient, string text, CancellationToken ct = default) => Locked(async () =>
    {
        var body = BMessage.BuildForPush(recipient, text);
        await NavigateAsync("telecom/msg", ct).ConfigureAwait(false);
        var p = new AppParameters().AddByte(MapTag.Charset, 1);
        var r = await Obex.PutAsync([ObexHeader.Name("outbox"), ObexHeader.Type(MapTypes.Message), p.ToHeader()], body, ct).ConfigureAwait(false);
        Ensure(r, "PushMessage");
        return r.Headers.GetText(ObexHeaderId.Name);
    }, ct);

    public Task SetReadStatusAsync(string handle, bool read, CancellationToken ct = default) => Locked(async () =>
    {
        var p = new AppParameters().AddByte(MapTag.StatusIndicator, 0).AddByte(MapTag.StatusValue, (byte)(read ? 1 : 0));
        // Filler byte 0x30 is mandated by MAP for PUTs without a real body.
        var r = await Obex.PutAsync([ObexHeader.Name(handle), ObexHeader.Type(MapTypes.MessageStatus), p.ToHeader()], [0x30], ct).ConfigureAwait(false);
        Ensure(r, "SetMessageStatus");
        return true;
    }, ct);

    public Task SetNotificationRegistrationAsync(bool enabled, CancellationToken ct = default) => Locked(async () =>
    {
        var p = new AppParameters().AddByte(MapTag.NotificationStatus, (byte)(enabled ? 1 : 0));
        var r = await Obex.PutAsync([ObexHeader.Type(MapTypes.NotificationRegistration), p.ToHeader()], [0x30], ct).ConfigureAwait(false);
        Ensure(r, "SetNotificationRegistration");
        return true;
    }, ct);

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_obex is null) return;
        try { await _obex.DisconnectAsync(ct).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or EndOfStreamException or TimeoutException or ObexProtocolException) { }
        await _obex.DisposeAsync().ConfigureAwait(false);
        _obex = null;
        SetState(BluetoothConnectionState.Disconnected);
    }

    private async void OnMnsEvent(object? sender, MapEvent e)
    {
        if (!e.Type.Equals("NewMessage", StringComparison.OrdinalIgnoreCase) || e.Handle is null) return;
        try
        {
            MessageReceived?.Invoke(this, await FetchNewMessageAsync(e, CancellationToken.None).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"MAP: could not fetch new message ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>Listing first (does not change read state, and iOS puts the text in subject); full GetMessage only as fallback.</summary>
    internal async Task<MapMessage> FetchNewMessageAsync(MapEvent e, CancellationToken ct)
    {
        string folder = string.IsNullOrEmpty(e.Folder) ? "telecom/msg/inbox" : e.Folder!;
        var listing = await GetMessagesListingAsync(folder.ToLowerInvariant(), 20, 255, ct).ConfigureAwait(false);
        var entry = listing.FirstOrDefault(m => m.Handle.Equals(e.Handle, StringComparison.OrdinalIgnoreCase));
        if (entry is not null && !string.IsNullOrEmpty(entry.Subject))
        {
            return new MapMessage
            {
                Handle = entry.Handle,
                SenderName = entry.SenderName,
                SenderAddress = entry.SenderAddressing,
                Body = entry.Subject,
                DateTime = entry.DateTime,
                Read = entry.Read,
                Type = entry.Type,
            };
        }
        // shortcut: no truncation check on the listing subject (iOS may cut at SubjectLength=255); upgrade if long texts arrive cut.
        var msg = await GetMessageAsync(e.Handle!, ct).ConfigureAwait(false);
        return new MapMessage
        {
            Handle = e.Handle!,
            SenderName = msg.Originator?.DisplayName ?? e.SenderName,
            SenderAddress = msg.Originator?.Phones.FirstOrDefault()?.Number ?? msg.Originator?.Emails.FirstOrDefault(),
            Body = msg.Body,
            DateTime = e.DateTime ?? entry?.DateTime,
            Read = msg.Status.Equals("READ", StringComparison.OrdinalIgnoreCase),
            Type = msg.Type,
        };
    }

    private ObexClient Obex => _obex ?? throw new InvalidOperationException("MAP client not connected");

    private async Task<T> Locked<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _op.WaitAsync(ct).ConfigureAwait(false);
        try { return await action().ConfigureAwait(false); }
        finally { _op.Release(); }
    }

    private static (string Parent, string Name) SplitFolder(string folder)
    {
        var parts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return ("telecom/msg", parts[0]);
        return (string.Join('/', parts[..^1]), parts.Length == 0 ? "" : parts[^1]);
    }

    /// <summary>MAP SetPath goes one level per request; skip when already there.</summary>
    private async Task NavigateAsync(string path, CancellationToken ct)
    {
        var target = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (target.SequenceEqual(_path, StringComparer.OrdinalIgnoreCase)) return;
        _path = [];
        Ensure(await Obex.SetPathAsync("", ct: ct).ConfigureAwait(false), "SetPath root");
        var walked = new List<string>();
        foreach (var part in target)
        {
            Ensure(await Obex.SetPathAsync(part, ct: ct).ConfigureAwait(false), $"SetPath (depth {walked.Count + 1})");
            walked.Add(part);
        }
        _path = [.. walked];
    }

    private void Ensure(ObexResponse r, string op)
    {
        if (r.IsSuccess) return;
        if (r.Code is ObexResponseCode.Forbidden or ObexResponseCode.Unauthorized) SetState(BluetoothConnectionState.PermissionDenied);
        throw new ObexException(r.Code, op);
    }

    private void SetState(BluetoothConnectionState s)
    {
        if (State == s) return;
        State = s;
        _log?.Invoke($"MAP state -> {s}");
        ConnectionStateChanged?.Invoke(this, s);
    }

    public async ValueTask DisposeAsync()
    {
        if (_mns is not null) _mns.EventReceived -= OnMnsEvent;
        await DisconnectAsync().ConfigureAwait(false);
        _op.Dispose();
    }
}
