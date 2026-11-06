using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace PhoneBridge.Bluetooth.Platform;

/// <summary>
/// BLE advertising that BlueFerry/iphonebridge found necessary on Linux: a "List of 128-bit Service Solicitation
/// UUIDs" (AD type 0x15) with ANCS, and optionally a connectable advert so the iPhone can connect to the PC.
/// Whether iOS reacts to it from a Windows PC is exactly what the spike measures.
/// </summary>
public sealed class AncsAdvertiser : IDisposable
{
    public const byte AdTypeSolicitation128 = 0x15;
    /// <summary>Random service UUID used only to obtain a connectable advert from Windows.</summary>
    public static readonly Guid PhoneBridgeBeaconService = new("6B1F3A52-6E1C-4C6B-9D52-3B1D2A7E0B11");

    private readonly BluetoothLEAdvertisementPublisher _publisher;
    private GattServiceProvider? _gattProvider;
    private readonly Action<string>? _log;

    public BluetoothLEAdvertisementPublisherStatus Status => _publisher.Status;

    public AncsAdvertiser(Action<string>? log = null)
    {
        _log = log;
        var adv = new BluetoothLEAdvertisement();
        adv.DataSections.Add(new BluetoothLEAdvertisementDataSection(AdTypeSolicitation128, WindowsBluetooth.ToBuffer(SolicitationPayload())));
        _publisher = new BluetoothLEAdvertisementPublisher(adv);
        _publisher.StatusChanged += (_, e) => _log?.Invoke($"BLE solicitation advert: {e.Status} (error {e.Error})");
    }

    /// <summary>128-bit UUIDs go little-endian in advertising data (reverse of the textual order).</summary>
    public static byte[] SolicitationPayload() => BluetoothIds.ObexUuid(BluetoothIds.AncsService.ToString()).Reverse().ToArray();

    public void Start() => _publisher.Start();

    /// <summary>Peripheral-role attempt: a connectable, discoverable advert carrying a dummy PhoneBridge GATT service.</summary>
    public async Task<bool> StartConnectableAsync()
    {
        var r = await GattServiceProvider.CreateAsync(PhoneBridgeBeaconService);
        if (r.Error != global::Windows.Devices.Bluetooth.BluetoothError.Success)
        {
            _log?.Invoke($"GattServiceProvider.CreateAsync failed: {r.Error}");
            return false;
        }
        _gattProvider = r.ServiceProvider;
        _gattProvider.AdvertisementStatusChanged += (_, e) => _log?.Invoke($"Connectable advert: {e.Status} (error {e.Error})");
        _gattProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
        return true;
    }

    public void Dispose()
    {
        try { _publisher.Stop(); } catch (Exception) { }
        try { _gattProvider?.StopAdvertising(); } catch (Exception) { }
    }
}
