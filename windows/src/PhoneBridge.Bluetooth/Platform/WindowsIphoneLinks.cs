using PhoneBridge.Bluetooth.Ancs;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Pbap;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;

namespace PhoneBridge.Bluetooth.Platform;

/// <summary>
/// Real iPhone links, same path as the spikes (tools/spike-ancs, tools/spike-map).
/// shortcut: picks the first paired device whose name contains "iPhone"; add a device picker if Arthur's is renamed.
/// </summary>
public sealed class WindowsIphoneLinks(Action<string>? log = null) : IIphoneLinks
{
    private AncsAdvertiser? _advertiser;
    private MnsServiceHost? _mnsHost;
    private BluetoothLEDevice? _le;
    private BluetoothDevice? _classic;
    private RfcommDeviceService? _mas, _pse;

    public async Task<IAncsClient> OpenAncsAsync(CancellationToken ct)
    {
        var (le, classic) = await FindIphoneAsync();
        StartAdvertiser();
        _le?.Dispose();
        _le = le is not null ? await BluetoothLEDevice.FromIdAsync(le.Id) : await BluetoothLEDevice.FromBluetoothAddressAsync(classic!.Address);
        if (_le is null) throw new BluetoothLinkException(BluetoothIssue.Unreachable, "LE device unavailable");
        ct.ThrowIfCancellationRequested();
        return new AncsSession(await GattAncsTransport.CreateAsync(_le, log), log);
    }

    public async Task<IMapClient> OpenMapAsync(MnsServer mns, CancellationToken ct)
    {
        var mas = await OpenServiceAsync(BluetoothIds.MapMasShortId, "MAS");
        _mas?.Dispose();
        _mas = mas;
        if (_mnsHost is null)
        {
            try { _mnsHost = await MnsServiceHost.StartAsync(mns, log); }
            catch (Exception e) { log?.Invoke($"MNS not published ({e.GetType().Name}): no real-time messages over MAP"); }
        }
        var map = new MapClient(c => WindowsBluetooth.ConnectAsync(mas, c), log);
        if (_mnsHost is not null) map.AttachMns(mns);
        return map;
    }

    public async Task<IPbapClient> OpenPbapAsync(CancellationToken ct)
    {
        var pse = await OpenServiceAsync(BluetoothIds.PbapPseShortId, "PBAP PSE");
        _pse?.Dispose();
        _pse = pse;
        return new PbapClient(c => WindowsBluetooth.ConnectAsync(pse, c), log);
    }

    private static async Task<(PairedDevice? Le, PairedDevice? Classic)> FindIphoneAsync()
    {
        var adapter = await WindowsBluetooth.GetAdapterAsync();
        if (adapter is null || adapter.RadioState == "Off") throw new BluetoothLinkException(BluetoothIssue.NoAdapter, "No Bluetooth adapter or radio off");
        var paired = await WindowsBluetooth.GetPairedDevicesAsync();
        var le = paired.FirstOrDefault(d => d.IsLowEnergy && d.LooksLikeIphone);
        var classic = paired.FirstOrDefault(d => !d.IsLowEnergy && d.LooksLikeIphone);
        if (le is null && classic is null) throw new BluetoothLinkException(BluetoothIssue.NotPaired, "No paired iPhone");
        return (le, classic);
    }

    private async Task<RfcommDeviceService> OpenServiceAsync(ushort shortId, string name)
    {
        var (_, classic) = await FindIphoneAsync();
        if (classic is null) throw new BluetoothLinkException(BluetoothIssue.NotPaired, "iPhone not paired over Classic");
        if (_classic is null || _classic.DeviceId != classic.Id)
        {
            _classic?.Dispose();
            _classic = await BluetoothDevice.FromIdAsync(classic.Id)
                       ?? throw new BluetoothLinkException(BluetoothIssue.Unreachable, "Classic device unavailable");
        }
        return await WindowsBluetooth.FindRfcommServiceAsync(_classic, shortId)
               ?? throw new BluetoothLinkException(BluetoothIssue.Unreachable, $"{name} not found");
    }

    /// <summary>The ANCS solicitation advert BlueFerry needed on Linux; harmless if Windows refuses it.</summary>
    private void StartAdvertiser()
    {
        if (_advertiser is not null) return;
        try
        {
            _advertiser = new AncsAdvertiser(log);
            _advertiser.Start();
        }
        catch (Exception e) { log?.Invoke($"ANCS solicitation advert failed ({e.GetType().Name})"); }
    }

    public async ValueTask DisposeAsync()
    {
        _advertiser?.Dispose();
        if (_mnsHost is not null) await _mnsHost.DisposeAsync();
        _mas?.Dispose();
        _pse?.Dispose();
        _le?.Dispose();
        _classic?.Dispose();
        (_advertiser, _mnsHost, _mas, _pse, _le, _classic) = (null, null, null, null, null, null);
    }
}
