using Windows.Networking.Sockets;

namespace PhoneBridge.Bluetooth.Platform;

/// <summary>Exposes a WinRT StreamSocket (RFCOMM) as one duplex .NET Stream; disposing closes the socket.</summary>
public sealed class SocketStream : Stream
{
    private readonly StreamSocket _socket;
    private readonly Stream _input;
    private readonly Stream _output;

    public SocketStream(StreamSocket socket)
    {
        _socket = socket;
        // Unbuffered: OBEX is request/response, every packet must leave immediately.
        _input = socket.InputStream.AsStreamForRead(0);
        _output = socket.OutputStream.AsStreamForWrite(0);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _input.ReadAsync(buffer, ct);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _input.ReadAsync(buffer, offset, count, ct);
    public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _output.WriteAsync(buffer, ct);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _output.WriteAsync(buffer, offset, count, ct);
    public override void Flush() => _output.Flush();
    public override Task FlushAsync(CancellationToken ct) => _output.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _input.Dispose();
            _output.Dispose();
            _socket.Dispose();
        }
        base.Dispose(disposing);
    }
}
