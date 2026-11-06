using PhoneBridge.Bluetooth.Ancs;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace PhoneBridge.Bluetooth.Platform;

/// <summary>ANCS characteristics on a real iPhone through the WinRT GATT client.</summary>
public sealed class GattAncsTransport : IAncsTransport
{
    private readonly BluetoothLEDevice _device;
    private readonly GattSession? _session;
    private readonly GattDeviceService _service;
    private readonly GattCharacteristic _notificationSource;
    private readonly GattCharacteristic _controlPoint;
    private readonly GattCharacteristic _dataSource;

    public event EventHandler<byte[]>? NotificationSourceReceived;
    public event EventHandler<byte[]>? DataSourceReceived;
    public event EventHandler<bool>? ConnectionChanged;

    private GattAncsTransport(BluetoothLEDevice device, GattSession? session, GattDeviceService service,
        GattCharacteristic ns, GattCharacteristic cp, GattCharacteristic ds)
    {
        _device = device;
        _session = session;
        _service = service;
        _notificationSource = ns;
        _controlPoint = cp;
        _dataSource = ds;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    /// <summary>Finds ANCS on the device. Throws AncsAccessDeniedException when the link is not bonded/encrypted.</summary>
    public static async Task<GattAncsTransport> CreateAsync(BluetoothLEDevice device, Action<string>? log = null)
    {
        GattSession? session = null;
        try
        {
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            session.MaintainConnection = true; // keep the LE link up between notifications
        }
        catch (Exception e) { log?.Invoke($"GattSession unavailable: {e.Message}"); }

        var sr = await device.GetGattServicesForUuidAsync(BluetoothIds.AncsService, BluetoothCacheMode.Uncached);
        if (sr.Status == GattCommunicationStatus.AccessDenied) throw new AncsAccessDeniedException("ANCS service access denied");
        if (sr.Status != GattCommunicationStatus.Success || sr.Services.Count == 0)
            throw new InvalidOperationException($"ANCS service not found (status {sr.Status}, {sr.Services.Count} service(s))");
        var service = sr.Services[0];

        var access = await service.RequestAccessAsync();
        log?.Invoke($"ANCS service access: {access}");

        var cr = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (cr.Status == GattCommunicationStatus.AccessDenied) throw new AncsAccessDeniedException("ANCS characteristics access denied");
        if (cr.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"ANCS characteristics: {cr.Status}");

        GattCharacteristic? Find(Guid id) => cr.Characteristics.FirstOrDefault(c => c.Uuid == id);
        var ns = Find(BluetoothIds.AncsNotificationSource) ?? throw new InvalidOperationException("ANCS Notification Source missing");
        var cp = Find(BluetoothIds.AncsControlPoint) ?? throw new InvalidOperationException("ANCS Control Point missing");
        var ds = Find(BluetoothIds.AncsDataSource) ?? throw new InvalidOperationException("ANCS Data Source missing");
        return new GattAncsTransport(device, session, service, ns, cp, ds);
    }

    public async Task SubscribeAsync(CancellationToken ct)
    {
        _dataSource.ValueChanged += OnDataSource;
        _notificationSource.ValueChanged += OnNotificationSource;
        // Data Source first: the iPhone starts sending (pre-existing) notifications as soon as NS is subscribed.
        await EnableNotifyAsync(_dataSource, "Data Source").ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await EnableNotifyAsync(_notificationSource, "Notification Source").ConfigureAwait(false);
    }

    private static async Task EnableNotifyAsync(GattCharacteristic c, string name)
    {
        var r = await c.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (r.Status == GattCommunicationStatus.Success) return;
        // 0x05 Insufficient Authentication / 0x0F Insufficient Encryption: not bonded over LE.
        if (r.Status == GattCommunicationStatus.AccessDenied || r.ProtocolError is 0x05 or 0x0F)
            throw new AncsAccessDeniedException($"{name} subscribe refused ({r.Status}, ATT 0x{r.ProtocolError ?? 0:X2})");
        throw new InvalidOperationException($"{name} subscribe failed ({r.Status}, ATT 0x{r.ProtocolError ?? 0:X2})");
    }

    public async Task WriteControlPointAsync(byte[] command, CancellationToken ct)
    {
        var r = await _controlPoint.WriteValueWithResultAsync(WindowsBluetooth.ToBuffer(command), GattWriteOption.WriteWithResponse).AsTask(ct);
        if (r.Status == GattCommunicationStatus.Success) return;
        throw new AncsControlPointException(r.ProtocolError, $"Control Point write failed ({r.Status}, 0x{r.ProtocolError ?? 0:X2})");
    }

    private void OnNotificationSource(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        NotificationSourceReceived?.Invoke(this, WindowsBluetooth.ToArray(args.CharacteristicValue));

    private void OnDataSource(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        DataSourceReceived?.Invoke(this, WindowsBluetooth.ToArray(args.CharacteristicValue));

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args) =>
        ConnectionChanged?.Invoke(this, sender.ConnectionStatus == BluetoothConnectionStatus.Connected);

    public ValueTask DisposeAsync()
    {
        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _dataSource.ValueChanged -= OnDataSource;
        _notificationSource.ValueChanged -= OnNotificationSource;
        _service.Dispose();
        _session?.Dispose();
        return ValueTask.CompletedTask;
    }
}
