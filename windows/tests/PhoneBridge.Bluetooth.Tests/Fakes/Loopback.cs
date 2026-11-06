using System.Net;
using System.Net.Sockets;

namespace PhoneBridge.Bluetooth.Tests.Fakes;

/// <summary>A connected pair of real duplex streams over 127.0.0.1, standing in for an RFCOMM socket.</summary>
public sealed class Loopback : IAsyncDisposable
{
    private readonly TcpClient _a;
    private readonly TcpClient _b;
    public Stream Client { get; }
    public Stream Server { get; }

    private Loopback(TcpClient a, TcpClient b)
    {
        _a = a;
        _b = b;
        Client = a.GetStream();
        Server = b.GetStream();
    }

    public static async Task<Loopback> CreateAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var client = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var server = await accept;
            client.NoDelay = server.NoDelay = true;
            return new Loopback(client, server);
        }
        finally { listener.Stop(); }
    }

    public ValueTask DisposeAsync()
    {
        _a.Dispose();
        _b.Dispose();
        return ValueTask.CompletedTask;
    }
}
