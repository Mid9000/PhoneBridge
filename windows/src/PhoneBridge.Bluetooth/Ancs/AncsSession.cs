using System.Threading.Channels;

namespace PhoneBridge.Bluetooth.Ancs;

/// <summary>The three ANCS characteristics, abstracted so the session logic runs against a fake iPhone in tests.</summary>
public interface IAncsTransport : IAsyncDisposable
{
    event EventHandler<byte[]>? NotificationSourceReceived;
    event EventHandler<byte[]>? DataSourceReceived;
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>Subscribes Data Source first, then Notification Source (so no response is lost).</summary>
    Task SubscribeAsync(CancellationToken ct);
    /// <summary>Write-with-response to the Control Point. Throws AncsControlPointException on an ANCS error code.</summary>
    Task WriteControlPointAsync(byte[] command, CancellationToken ct);
}

public sealed class AncsControlPointException(byte? errorCode, string message) : Exception(message)
{
    public byte? ErrorCode { get; } = errorCode;
}

public sealed class AncsAccessDeniedException(string message) : Exception(message);

/// <summary>
/// ANCS consumer logic: one Control Point command in flight at a time (Data Source responses carry no
/// request id), app display names cached, events raised on the worker task.
/// </summary>
public sealed class AncsSession : IAncsClient
{
    private readonly IAncsTransport _transport;
    private readonly Action<string>? _log;
    private readonly Channel<AncsNotificationSourceEvent> _queue = Channel.CreateUnbounded<AncsNotificationSourceEvent>(new() { SingleReader = true });
    private readonly AncsDataSourceReassembler _reassembler = new();
    private readonly Dictionary<string, string> _appNames = new();
    private readonly object _lock = new();
    private TaskCompletionSource<AncsAttributeResponse>? _pending;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public BluetoothConnectionState State { get; private set; } = BluetoothConnectionState.Disconnected;

    public event EventHandler<AncsNotification>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved;
    public event EventHandler<AncsNotification>? IncomingCall;
    public event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;

    public AncsSession(IAncsTransport transport, Action<string>? log = null)
    {
        _transport = transport;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        SetState(BluetoothConnectionState.Connecting);
        _transport.DataSourceReceived += OnDataSource;
        _transport.NotificationSourceReceived += OnNotificationSource;
        _transport.ConnectionChanged += OnConnectionChanged;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _worker = Task.Run(() => WorkerAsync(_cts.Token));
        try
        {
            await _transport.SubscribeAsync(ct).ConfigureAwait(false);
            SetState(BluetoothConnectionState.Connected);
        }
        catch (AncsAccessDeniedException)
        {
            SetState(BluetoothConnectionState.PermissionDenied);
            throw;
        }
        catch
        {
            SetState(BluetoothConnectionState.Unavailable);
            throw;
        }
    }

    public Task PerformActionAsync(uint notificationUid, AncsAction action, CancellationToken ct = default) =>
        _transport.WriteControlPointAsync(AncsCommands.PerformNotificationAction(notificationUid, action), ct);

    private void OnConnectionChanged(object? sender, bool connected) =>
        SetState(connected ? BluetoothConnectionState.Connected : BluetoothConnectionState.Disconnected);

    private void SetState(BluetoothConnectionState s)
    {
        if (State == s) return;
        State = s;
        _log?.Invoke($"ANCS state -> {s}");
        ConnectionStateChanged?.Invoke(this, s);
    }

    private void OnNotificationSource(object? sender, byte[] data)
    {
        try { _queue.Writer.TryWrite(AncsNotificationSourceEvent.Parse(data)); }
        catch (FormatException e) { _log?.Invoke($"ANCS: bad Notification Source packet ({e.Message})"); }
    }

    private void OnDataSource(object? sender, byte[] data)
    {
        TaskCompletionSource<AncsAttributeResponse>? done = null;
        AncsAttributeResponse? response;
        lock (_lock)
        {
            response = _reassembler.Append(data);
            if (response is not null) { done = _pending; _pending = null; }
        }
        if (response is not null) done?.TrySetResult(response);
    }

    private async Task WorkerAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var ev in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try { await HandleAsync(ev, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception e) { _log?.Invoke($"ANCS: uid {ev.Uid} skipped: {e.GetType().Name}: {e.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task HandleAsync(AncsNotificationSourceEvent ev, CancellationToken ct)
    {
        if (ev.EventId == AncsEventId.Removed)
        {
            NotificationRemoved?.Invoke(this, ev.Uid);
            return;
        }

        var attrs = AncsCommands.DefaultNotificationAttributes;
        var response = await RequestAsync(AncsCommands.GetNotificationAttributes(ev.Uid, attrs), AncsCommandId.GetNotificationAttributes, attrs.Length, ct).ConfigureAwait(false);
        string Attr(AncsNotificationAttribute a) => response.Attributes.TryGetValue((byte)a, out var v) ? v : "";

        string appId = Attr(AncsNotificationAttribute.AppIdentifier);
        var n = new AncsNotification
        {
            Uid = ev.Uid,
            Category = ev.Category,
            Flags = ev.Flags,
            IsModification = ev.EventId == AncsEventId.Modified,
            AppIdentifier = appId,
            AppDisplayName = appId.Length > 0 ? await GetAppNameAsync(appId, ct).ConfigureAwait(false) : null,
            Title = NullIfEmpty(Attr(AncsNotificationAttribute.Title)),
            Subtitle = NullIfEmpty(Attr(AncsNotificationAttribute.Subtitle)),
            Message = NullIfEmpty(Attr(AncsNotificationAttribute.Message)),
            Date = AncsCommands.ParseDate(Attr(AncsNotificationAttribute.Date)),
            PositiveActionLabel = NullIfEmpty(Attr(AncsNotificationAttribute.PositiveActionLabel)),
            NegativeActionLabel = NullIfEmpty(Attr(AncsNotificationAttribute.NegativeActionLabel)),
        };
        _log?.Invoke($"ANCS: uid {n.Uid} {ev.EventId} category={n.Category} app={n.AppIdentifier} flags={n.Flags}");
        NotificationReceived?.Invoke(this, n);
        if (n.Category == AncsCategory.IncomingCall && ev.EventId == AncsEventId.Added) IncomingCall?.Invoke(this, n);
    }

    private async Task<string?> GetAppNameAsync(string appId, CancellationToken ct)
    {
        lock (_lock) if (_appNames.TryGetValue(appId, out var cached)) return cached;
        try
        {
            var r = await RequestAsync(AncsCommands.GetAppAttributes(appId, [new((byte)AncsAppAttribute.DisplayName)]), AncsCommandId.GetAppAttributes, 1, ct).ConfigureAwait(false);
            var name = r.Attributes.GetValueOrDefault((byte)AncsAppAttribute.DisplayName);
            if (!string.IsNullOrEmpty(name)) lock (_lock) _appNames[appId] = name;
            return NullIfEmpty(name ?? "");
        }
        catch (Exception e) when (e is TimeoutException or AncsControlPointException)
        {
            return null; // the notification is still useful without the display name
        }
    }

    private async Task<AncsAttributeResponse> RequestAsync(byte[] command, AncsCommandId id, int attributeCount, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<AncsAttributeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _reassembler.Expect(id, attributeCount);
            _pending = tcs;
        }
        try
        {
            await _transport.WriteControlPointAsync(command, ct).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(ResponseTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (_pending == tcs) { _pending = null; _reassembler.Reset(); }
            }
        }
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    public async ValueTask DisposeAsync()
    {
        _transport.DataSourceReceived -= OnDataSource;
        _transport.NotificationSourceReceived -= OnNotificationSource;
        _transport.ConnectionChanged -= OnConnectionChanged;
        _queue.Writer.TryComplete();
        _cts?.Cancel();
        if (_worker is not null) { try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        _cts?.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
