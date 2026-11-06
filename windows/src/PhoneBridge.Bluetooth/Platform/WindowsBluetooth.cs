using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PhoneBridge.Bluetooth.Platform;

public sealed record AdapterInfo(string Address, bool ClassicSupported, bool LowEnergySupported, bool CentralRoleSupported,
    bool PeripheralRoleSupported, bool AdvertisementOffloadSupported, int MaxAdvertisementDataLength, string RadioState);

public sealed record PairedDevice(string Id, string Name, ulong Address, bool IsLowEnergy, bool IsConnected)
{
    public string AddressText => FormatAddress(Address);
    public bool LooksLikeIphone => Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase);

    public static string FormatAddress(ulong a) =>
        string.Join(":", Enumerable.Range(0, 6).Select(i => ((a >> (8 * (5 - i))) & 0xFF).ToString("X2")));
}

/// <summary>Thin WinRT helpers shared by the spikes and the future Host. Everything here needs real hardware.</summary>
public static class WindowsBluetooth
{
    public static async Task<AdapterInfo?> GetAdapterAsync()
    {
        var a = await BluetoothAdapter.GetDefaultAsync();
        if (a is null) return null;
        string radio = "unknown";
        try { radio = (await a.GetRadioAsync())?.State.ToString() ?? "none"; } catch (Exception e) { radio = "error: " + e.Message; }
        return new AdapterInfo(PairedDevice.FormatAddress(a.BluetoothAddress), a.IsClassicSupported, a.IsLowEnergySupported,
            a.IsCentralRoleSupported, a.IsPeripheralRoleSupported, a.IsAdvertisementOffloadSupported,
            (int)a.MaxAdvertisementDataLength, radio);
    }

    public static async Task<IReadOnlyList<PairedDevice>> GetPairedDevicesAsync()
    {
        var result = new List<PairedDevice>();
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
        {
            using var d = await BluetoothDevice.FromIdAsync(info.Id);
            if (d is not null) result.Add(new PairedDevice(info.Id, d.Name, d.BluetoothAddress, false, d.ConnectionStatus == BluetoothConnectionStatus.Connected));
        }
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)))
        {
            using var d = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (d is not null) result.Add(new PairedDevice(info.Id, d.Name, d.BluetoothAddress, true, d.ConnectionStatus == BluetoothConnectionStatus.Connected));
        }
        return result;
    }

    /// <summary>All RFCOMM services from a fresh SDP query (Uncached, so iOS toggles changes are seen).</summary>
    public static async Task<IReadOnlyList<RfcommDeviceService>> GetRfcommServicesAsync(BluetoothDevice device)
    {
        var r = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
        if (r.Error != BluetoothError.Success) throw new InvalidOperationException($"SDP query failed: {r.Error}");
        return r.Services;
    }

    public static async Task<RfcommDeviceService?> FindRfcommServiceAsync(BluetoothDevice device, ushort shortId)
    {
        var r = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromShortId(shortId), BluetoothCacheMode.Uncached);
        return r.Error == BluetoothError.Success ? r.Services.FirstOrDefault() : null;
    }

    public static async Task<IReadOnlyDictionary<uint, SdpElement>> ReadSdpAsync(RfcommDeviceService service)
    {
        var raw = await service.GetSdpRawAttributesAsync(BluetoothCacheMode.Uncached);
        var result = new Dictionary<uint, SdpElement>();
        foreach (var (id, buffer) in raw)
        {
            try { result[id] = Sdp.Parse(ToArray(buffer)); }
            catch (FormatException) { result[id] = new SdpElement(SdpElement.TypeNil); }
        }
        return result;
    }

    public static async Task<Stream> ConnectAsync(RfcommDeviceService service, CancellationToken ct = default)
    {
        var socket = new StreamSocket();
        try
        {
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct);
            return new SocketStream(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static byte[] ToArray(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    public static IBuffer ToBuffer(byte[] bytes)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(bytes);
        return writer.DetachBuffer();
    }
}
