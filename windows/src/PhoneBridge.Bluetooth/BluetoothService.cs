using PhoneBridge.Bluetooth.Ancs;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Obex;
using PhoneBridge.Bluetooth.Pbap;
using PhoneBridge.Core;

namespace PhoneBridge.Bluetooth;

/// <summary>Why the Bluetooth link is not (fully) up. UI contract codes (docs/ui-bridge.md, Connection.bluetoothIssue).</summary>
public static class BluetoothIssue
{
    public const string NoAdapter = "noAdapter", NotPaired = "notPaired", NotificationsDenied = "notificationsDenied",
        MessagesDenied = "messagesDenied", Unreachable = "unreachable";
}

public sealed class BluetoothLinkException(string issue, string message) : Exception(message)
{
    public string Issue { get; } = issue;
}

/// <summary>Opens the iPhone links. Real hardware: Platform.WindowsIphoneLinks; tests: fakes over loopback.</summary>
public interface IIphoneLinks : IAsyncDisposable
{
    /// <summary>Returned not started: the caller subscribes, then calls StartAsync.</summary>
    Task<IAncsClient> OpenAncsAsync(CancellationToken ct);
    /// <summary>Returned not connected, fed by <paramref name="mns"/> events when the platform could publish MNS.</summary>
    Task<IMapClient> OpenMapAsync(MnsServer mns, CancellationToken ct);
    Task<IPbapClient> OpenPbapAsync(CancellationToken ct);
}

/// <summary>A live iPhone notification. Id is the database id (negative when history is off). Content: never log it.</summary>
public sealed record PhoneNotice(long Id, uint Uid, string AppId, string? AppName, string Category, string? Title, string? Subtitle,
    string? Body, DateTimeOffset At, string? PositiveAction, string? NegativeAction, bool Ringing, bool Silent);

/// <summary>PBAP contacts, in memory only (never written to disk), looked up by phone number or email.</summary>
public sealed class ContactBook
{
    private readonly Dictionary<string, string> _names = new();

    public void Load(IEnumerable<VCard> cards)
    {
        foreach (var card in cards)
        {
            var name = card.DisplayName;
            foreach (var handle in card.Phones.Select(p => p.Number).Concat(card.Emails))
                if (name.Length > 0 && name != handle) lock (_names) _names[Key(handle)] = name;
        }
    }

    /// <summary>Remembers a sender name given by MAP; PBAP names win.</summary>
    public void Learn(string handle, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && name != handle) lock (_names) _names.TryAdd(Key(handle), name.Trim());
    }

    public string? NameFor(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle)) return null;
        lock (_names) return _names.GetValueOrDefault(Key(handle));
    }

    private static string Key(string handle)
    {
        if (handle.Contains('@')) return handle.Trim().ToLowerInvariant();
        var digits = new string(handle.Where(char.IsAsciiDigit).ToArray());
        // Last 9 digits: "+33 6 12 34 56 78" and "06 12 34 56 78" are the same French mobile.
        return digits.Length >= 9 ? digits[^9..] : digits.Length > 0 ? digits : handle.Trim().ToLowerInvariant();
    }
}

/// <summary>
/// Bluetooth channel B (SPEC §5-6): ANCS notifications and calls, MAP messages (receive, send, read state), PBAP names.
/// Never throws to the Host: every failure becomes State/Issue and a retry every RetryInterval.
/// Also the "bluetooth" IReplyChannel: true only when MAP handed the message to the iPhone.
/// </summary>
public sealed class BluetoothService : IReplyChannel, IAsyncDisposable
{
    private const string MessagesApp = "com.apple.MobileSMS";

    private readonly IIphoneLinks _links;
    private readonly Database _db;
    private readonly AppSettings _settings;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time;
    private readonly MnsServer _mns;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<uint, long> _idsByUid = new();
    private readonly Dictionary<long, PhoneNotice> _active = new();
    private readonly Dictionary<string, string> _unreadPeerByHandle = new();
    private IAncsClient? _ancs;
    private IMapClient? _map;
    private bool _mapEvents, _contactsLoaded;
    private string? _ancsIssue, _mapIssue;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public BluetoothService(IIphoneLinks links, Database db, AppSettings settings, Action<string>? log = null, TimeProvider? time = null)
    {
        (_links, _db, _settings, _log, _time) = (links, db, settings, log, time ?? TimeProvider.System);
        _mns = new MnsServer(log);
        _mns.EventReceived += OnMnsEvent;
    }

    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(60);
    public ContactBook Contacts { get; } = new();
    public string Name => "bluetooth";
    public bool Running => _cts is not null;

    /// <summary>UI LinkState: off, connecting, connected, unavailable.</summary>
    public string State
    {
        get
        {
            if (!Running) return "off";
            if (_ancs?.State == BluetoothConnectionState.Connected || _map?.State == BluetoothConnectionState.Connected) return "connected";
            return AncsIssue is not null && MapIssue is not null ? "unavailable" : "connecting";
        }
    }

    /// <summary>Most actionable problem (BluetoothIssue code), also while partly connected; null when all is fine or off.</summary>
    public string? Issue => Running ? IssuePriority.FirstOrDefault(i => i == AncsIssue || i == MapIssue) : null;

    private static readonly string[] IssuePriority =
        [BluetoothIssue.NoAdapter, BluetoothIssue.NotPaired, BluetoothIssue.NotificationsDenied, BluetoothIssue.MessagesDenied, BluetoothIssue.Unreachable];

    private string? AncsIssue => LinkIssue(_ancs?.State, _ancsIssue, BluetoothIssue.NotificationsDenied);
    private string? MapIssue => LinkIssue(_map?.State, _mapIssue, BluetoothIssue.MessagesDenied);

    private static string? LinkIssue(BluetoothConnectionState? state, string? openIssue, string denied) => state switch
    {
        BluetoothConnectionState.Connected or BluetoothConnectionState.Connecting => null,
        BluetoothConnectionState.PermissionDenied => denied,
        null => openIssue,
        _ => openIssue ?? BluetoothIssue.Unreachable,
    };

    public event Action? StateChanged;
    public event Action<PhoneNotice>? NotificationAdded;
    public event Action<PhoneNotice>? NotificationRemoved;
    /// <summary>The iPhone resent its whole notification list (new ANCS session): reload it.</summary>
    public event Action? NotificationsReset;
    public event Action<PhoneNotice>? IncomingCall;
    /// <summary>bool = still unread on the iPhone.</summary>
    public event Action<MessageRecord, bool>? MessageReceived;
    /// <summary>Peer whose messages were all read on the iPhone.</summary>
    public event Action<string>? ConversationRead;
    public event Action<string>? OtpDetected;
    public event Action? ContactsLoaded;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => LoopAsync(ct));
        Log("Bluetooth: started");
        Raise(StateChanged);
    }

    public async Task StopAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null) return;
        cts.Cancel();
        try { if (_loop is not null) await _loop; } catch (Exception) { /* loop never throws; belt and braces */ }
        await _gate.WaitAsync();
        try { await CloseAsync(); }
        finally { _gate.Release(); }
        cts.Dispose();
        Log("Bluetooth: stopped");
        Raise(StateChanged);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await EnsureConnectedAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) { Log($"Bluetooth: connect pass failed ({e.GetType().Name})"); }
            try { await Task.Delay(RetryInterval, _time, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One connection pass: opens whatever link is down. Public for --bluetooth-smoke and tests.</summary>
    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!IsUp(_ancs?.State)) await OpenAncsAsync(ct);
            if (!IsUp(_map?.State)) await OpenMapAsync(ct);
            if (!_contactsLoaded && _mapIssue is not (BluetoothIssue.NoAdapter or BluetoothIssue.NotPaired)) await LoadContactsAsync(ct);
        }
        finally
        {
            _gate.Release();
            Raise(StateChanged);
        }
    }

    private static bool IsUp(BluetoothConnectionState? s) => s is BluetoothConnectionState.Connected or BluetoothConnectionState.Connecting;

    private async Task OpenAncsAsync(CancellationToken ct)
    {
        await DisposeQuietly(Interlocked.Exchange(ref _ancs, null));
        IAncsClient? ancs = null;
        try
        {
            ancs = await _links.OpenAncsAsync(ct);
            ancs.NotificationReceived += OnAncsNotification;
            ancs.NotificationRemoved += OnAncsRemoved;
            ancs.ConnectionStateChanged += OnLinkState;
            ResetNotifications();   // a new session resends every current notification as pre-existing
            await ancs.StartAsync(ct);
            (_ancs, _ancsIssue) = (ancs, null);
            Log("Bluetooth: ANCS connected");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DisposeQuietly(ancs);
            throw;
        }
        catch (Exception e)
        {
            await DisposeQuietly(ancs);
            _ancsIssue = Classify(e, BluetoothIssue.NotificationsDenied);
            Log($"Bluetooth: ANCS unavailable ({_ancsIssue}, {e.GetType().Name})");
        }
    }

    private async Task OpenMapAsync(CancellationToken ct)
    {
        await DisposeQuietly(Interlocked.Exchange(ref _map, null));
        IMapClient? map = null;
        try
        {
            map = await _links.OpenMapAsync(_mns, ct);
            map.MessageReceived += OnMapMessage;
            map.ConnectionStateChanged += OnLinkState;
            await map.ConnectAsync(ct);
            try
            {
                await map.SetNotificationRegistrationAsync(true, ct);
                _mapEvents = true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _mapEvents = false;   // sending still works; incoming texts then come from ANCS only
                Log($"Bluetooth: MAP event registration refused ({e.GetType().Name})");
            }
            (_map, _mapIssue) = (map, null);
            Log("Bluetooth: MAP connected");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DisposeQuietly(map);
            throw;
        }
        catch (Exception e)
        {
            await DisposeQuietly(map);
            _mapIssue = Classify(e, BluetoothIssue.MessagesDenied);
            Log($"Bluetooth: MAP unavailable ({_mapIssue}, {e.GetType().Name})");
        }
    }

    private async Task LoadContactsAsync(CancellationToken ct)
    {
        IPbapClient? pbap = null;
        try
        {
            pbap = await _links.OpenPbapAsync(ct);
            await pbap.ConnectAsync(ct);
            var cards = await pbap.PullPhonebookAsync(offset: 1, ct: ct);   // offset 1 skips the owner card
            Contacts.Load(cards);
            _contactsLoaded = true;
            Log($"Bluetooth: {cards.Count} contacts loaded");
            Raise(ContactsLoaded);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { Log($"Bluetooth: contacts unavailable ({Classify(e, "contactsDenied")}, {e.GetType().Name})"); }
        finally { await DisposeQuietly(pbap); }
    }

    private static string Classify(Exception e, string denied) => e switch
    {
        BluetoothLinkException l => l.Issue,
        AncsAccessDeniedException or UnauthorizedAccessException => denied,
        ObexException { Code: ObexResponseCode.Forbidden or ObexResponseCode.Unauthorized } => denied,
        _ => BluetoothIssue.Unreachable,
    };

    private void OnLinkState(object? sender, BluetoothConnectionState s) => Raise(StateChanged);

    // ------------------------------------------------------------------ ANCS

    private void ResetNotifications()
    {
        lock (_lock) { _idsByUid.Clear(); _active.Clear(); }
        _db.ClearNotifications();
        Raise(NotificationsReset);
    }

    private void OnAncsNotification(object? sender, AncsNotification n)
    {
        PhoneNotice? old = null;
        lock (_lock) if (_idsByUid.TryGetValue(n.Uid, out var oldId)) old = _active.GetValueOrDefault(oldId);
        if (old is not null) Forget(old);   // modification: replace the row

        var category = Category(n);
        var at = n.Date is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local)) : _time.GetUtcNow();
        long id = _settings.KeepHistory
            ? _db.AddNotification(new NotificationRecord(0, n.AppIdentifier, n.AppDisplayName, category, n.Title, n.Message, at)).Id
            : -(long)n.Uid - 1;   // history off: nothing on disk, a stable in-memory id instead
        var silent = n.IsPreExisting || n.IsModification || n.Flags.HasFlag(AncsEventFlags.Silent)
                     || (n.AppIdentifier == MessagesApp && _mapEvents);   // MAP already delivers (and toasts) texts
        var notice = new PhoneNotice(id, n.Uid, n.AppIdentifier, n.AppDisplayName, category, n.Title, n.Subtitle, n.Message, at,
            n.Flags.HasFlag(AncsEventFlags.PositiveAction) ? n.PositiveActionLabel : null,
            n.Flags.HasFlag(AncsEventFlags.NegativeAction) ? n.NegativeActionLabel : null,
            n.Category == AncsCategory.IncomingCall, silent);
        lock (_lock) { _idsByUid[n.Uid] = id; _active[id] = notice; }

        Raise(NotificationAdded, notice);
        if (notice.Ringing && !n.IsModification) Raise(IncomingCall, notice);
        if (!silent && _settings.OtpAutoCopy && OtpDetector.Detect(n.Message) is { } code) Raise(OtpDetected, code);
    }

    private void OnAncsRemoved(object? sender, uint uid)
    {
        PhoneNotice? notice = null;
        lock (_lock) if (_idsByUid.TryGetValue(uid, out var id)) notice = _active.GetValueOrDefault(id);
        if (notice is not null) Forget(notice);
    }

    private void Forget(PhoneNotice n)
    {
        lock (_lock) { _idsByUid.Remove(n.Uid); _active.Remove(n.Id); }
        _db.DeleteNotification(n.Id);
        Raise(NotificationRemoved, n);
    }

    private static string Category(AncsNotification n) => n.AppIdentifier == MessagesApp ? "message" : n.Category switch
    {
        AncsCategory.IncomingCall or AncsCategory.MissedCall or AncsCategory.Voicemail => "call",
        AncsCategory.Social => "social",
        AncsCategory.Email => "email",
        AncsCategory.News => "news",
        _ => "other",
    };

    public PhoneNotice? Find(long id)
    {
        lock (_lock) return _active.GetValueOrDefault(id);
    }

    public IReadOnlyList<PhoneNotice> RingingCalls()
    {
        lock (_lock) return _active.Values.Where(n => n.Ringing).ToList();
    }

    /// <summary>ANCS positive/negative action (answer/decline a call, clear a notification). false when impossible.</summary>
    public async Task<bool> PerformActionAsync(long id, bool positive, CancellationToken ct = default)
    {
        var n = Find(id);
        if (n is null || _ancs is not { State: BluetoothConnectionState.Connected } ancs) return false;
        if ((positive ? n.PositiveAction : n.NegativeAction) is null) return false;
        try
        {
            await ancs.PerformActionAsync(n.Uid, positive ? AncsAction.Positive : AncsAction.Negative, ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log($"Bluetooth: ANCS action failed ({e.GetType().Name})");
            return false;
        }
    }

    // ------------------------------------------------------------------ MAP

    private void OnMapMessage(object? sender, MapMessage m)
    {
        var peer = (m.SenderAddress ?? m.SenderName ?? "").Trim();
        if (peer.Length == 0) return;
        Contacts.Learn(peer, m.SenderName);
        // iOS reports SMS_GSM for iMessage too: keep the conversation's known service, else "sms".
        var service = _db.ListMessages(peer, 1).FirstOrDefault()?.Service ?? "sms";
        var msg = new MessageRecord(0, peer, m.Body ?? "", service, "in", m.DateTime ?? _time.GetUtcNow());
        if (_settings.KeepHistory) msg = _db.AddMessage(msg);
        if (!m.Read) lock (_lock) _unreadPeerByHandle[m.Handle] = peer;
        Log("Bluetooth: message received");
        Raise(MessageReceived, msg, !m.Read);
        if (_settings.OtpAutoCopy && OtpDetector.Detect(m.Body) is { } code) Raise(OtpDetected, code);
    }

    /// <summary>[À VÉRIFIER] whether iOS sends ReadStatusChanged (MAP 1.2+). Assumed to mean "read".</summary>
    private void OnMnsEvent(object? sender, MapEvent e)
    {
        if (!e.Type.Equals("ReadStatusChanged", StringComparison.OrdinalIgnoreCase) || e.Handle is null) return;
        string? peer;
        lock (_lock)
        {
            if (!_unreadPeerByHandle.Remove(e.Handle, out peer) || _unreadPeerByHandle.ContainsValue(peer)) return;
        }
        Raise(ConversationRead, peer);
    }

    /// <summary>Opened on the PC: mark the conversation's MAP messages read on the iPhone. Never throws.</summary>
    public async Task MarkConversationReadAsync(string peer, CancellationToken ct = default)
    {
        if (_map is not { State: BluetoothConnectionState.Connected } map) return;
        List<string> handles;
        lock (_lock)
        {
            handles = _unreadPeerByHandle.Where(p => p.Value == peer).Select(p => p.Key).ToList();
            foreach (var h in handles) _unreadPeerByHandle.Remove(h);
        }
        foreach (var handle in handles)
        {
            try { await map.SetReadStatusAsync(handle, true, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { Log($"Bluetooth: read status not synced ({e.GetType().Name})"); }
        }
    }

    public async Task<bool> TrySendAsync(string to, string body, CancellationToken ct)
    {
        if (_map is not { State: BluetoothConnectionState.Connected } map || !LooksLikeAddress(to)) return false;
        await map.PushMessageAsync(to.Trim(), body, ct);   // throws on refusal: ReplyQueue then falls back to the relay
        Log("Bluetooth: message handed to the iPhone");
        return true;
    }

    /// <summary>MAP needs a number or an email; a display name (from the Shortcuts relay) goes through the relay instead.</summary>
    internal static bool LooksLikeAddress(string to) =>
        to.Contains('@') || (to.Count(char.IsAsciiDigit) >= 3 && to.Trim().All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '.' or '(' or ')'));

    // ------------------------------------------------------------------ plumbing

    private async Task CloseAsync()
    {
        await DisposeQuietly(Interlocked.Exchange(ref _ancs, null));
        await DisposeQuietly(Interlocked.Exchange(ref _map, null));
        try { await _links.DisposeAsync(); } catch (Exception e) { Log($"Bluetooth: platform cleanup failed ({e.GetType().Name})"); }
        (_ancsIssue, _mapIssue, _mapEvents) = (null, null, false);
        lock (_lock) { _idsByUid.Clear(); _active.Clear(); _unreadPeerByHandle.Clear(); }
    }

    private async Task DisposeQuietly(IAsyncDisposable? d)
    {
        if (d is null) return;
        try { await d.DisposeAsync(); }
        catch (Exception e) { Log($"Bluetooth: dispose failed ({e.GetType().Name})"); }
    }

    private void Log(string line) => _log?.Invoke(line);

    private void Raise(Action? handler)
    {
        try { handler?.Invoke(); } catch (Exception e) { Log($"Bluetooth: handler failed ({e.GetType().Name})"); }
    }

    private void Raise<T>(Action<T>? handler, T arg)
    {
        try { handler?.Invoke(arg); } catch (Exception e) { Log($"Bluetooth: handler failed ({e.GetType().Name})"); }
    }

    private void Raise<T1, T2>(Action<T1, T2>? handler, T1 a, T2 b)
    {
        try { handler?.Invoke(a, b); } catch (Exception e) { Log($"Bluetooth: handler failed ({e.GetType().Name})"); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _mns.EventReceived -= OnMnsEvent;
    }
}
