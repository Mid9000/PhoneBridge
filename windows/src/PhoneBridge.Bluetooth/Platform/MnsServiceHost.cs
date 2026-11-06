using PhoneBridge.Bluetooth.Map;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace PhoneBridge.Bluetooth.Platform;

/// <summary>
/// Publishes an MNS SDP record (0x1133) over RFCOMM and hands every incoming iPhone connection to an MnsServer.
/// Must be running before SetNotificationRegistration(on): the iPhone looks the record up via SDP then.
/// </summary>
public sealed class MnsServiceHost : IAsyncDisposable
{
    private readonly RfcommServiceProvider _provider;
    private readonly StreamSocketListener _listener;
    private readonly CancellationTokenSource _cts = new();

    private MnsServiceHost(RfcommServiceProvider provider, StreamSocketListener listener)
    {
        _provider = provider;
        _listener = listener;
    }

    public static async Task<MnsServiceHost> StartAsync(MnsServer server, Action<string>? log = null)
    {
        var provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(BluetoothIds.MapMnsShortId));
        var listener = new StreamSocketListener();
        var host = new MnsServiceHost(provider, listener);
        listener.ConnectionReceived += (sender, e) => _ = host.ServeAsync(server, e.Socket, log);
        await listener.BindServiceNameAsync(provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

        void TrySet(uint id, byte[] value)
        {
            try { provider.SdpRawAttributes[id] = WindowsBluetooth.ToBuffer(value); }
            catch (Exception e) { log?.Invoke($"MNS SDP attribute {Sdp.AttributeName(id)} rejected by Windows: {e.Message}"); }
        }
        TrySet(0x0100, Sdp.Text("PhoneBridge MNS"));
        TrySet(0x0009, Sdp.ProfileDescriptorList(BluetoothIds.MapProfileShortId, 0x0104)); // MAP 1.4
        TrySet(0x0317, Sdp.UInt32((uint)MapFeatures.PhoneBridgeClient));

        provider.StartAdvertising(listener, false);
        log?.Invoke($"MNS published on RFCOMM ({provider.ServiceId.AsString()})");
        return host;
    }

    private async Task ServeAsync(MnsServer server, StreamSocket socket, Action<string>? log)
    {
        try
        {
            await using var stream = new SocketStream(socket);
            await server.RunConnectionAsync(stream, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { log?.Invoke($"MNS session error: {e.GetType().Name}: {e.Message}"); }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _provider.StopAdvertising(); } catch (Exception) { }
        _listener.Dispose();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
