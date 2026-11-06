using System.Text;
using PhoneBridge.Bluetooth.Obex;

namespace PhoneBridge.Bluetooth.Pbap;

/// <summary>PBAP 1.2 application parameter tags (§6.2.1).</summary>
public static class PbapTag
{
    public const byte Order = 0x01;
    public const byte SearchValue = 0x02;
    public const byte SearchProperty = 0x03;
    public const byte MaxListCount = 0x04;       // uint16; 0 = "just tell me the size"
    public const byte ListStartOffset = 0x05;    // uint16
    public const byte PropertySelector = 0x06;   // uint64
    public const byte Format = 0x07;             // 0 vCard 2.1, 1 vCard 3.0
    public const byte PhonebookSize = 0x08;      // uint16 (response)
    public const byte NewMissedCalls = 0x09;
    public const byte PbapSupportedFeatures = 0x10; // uint32
}

/// <summary>PBAP client (PCE). Paths: telecom/pb.vcf (contacts), telecom/ich|och|mch|cch.vcf (call history).</summary>
public sealed class PbapClient : IPbapClient
{
    public const string PhonebookType = "x-bt/phonebook";
    // Download + Browsing. Sent at connect as PBAP 1.2 requires when the PSE advertises its own features.
    public const uint SupportedFeatures = 0x00000003;

    private readonly Func<CancellationToken, Task<Stream>> _openStream;
    private readonly Action<string>? _log;
    private ObexClient? _obex;

    public BluetoothConnectionState State { get; private set; } = BluetoothConnectionState.Disconnected;

    public PbapClient(Func<CancellationToken, Task<Stream>> openStream, Action<string>? log = null)
    {
        _openStream = openStream;
        _log = log;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        State = BluetoothConnectionState.Connecting;
        try
        {
            _obex = new ObexClient(await _openStream(ct).ConfigureAwait(false), _log);
            var p = new AppParameters().AddUInt32(PbapTag.PbapSupportedFeatures, SupportedFeatures);
            var r = await _obex.ConnectAsync(BluetoothIds.PbapTarget, [p.ToHeader()], ct).ConfigureAwait(false);
            if (!r.IsSuccess)
            {
                State = r.Code is ObexResponseCode.Forbidden or ObexResponseCode.Unauthorized ? BluetoothConnectionState.PermissionDenied : BluetoothConnectionState.Unavailable;
                throw new ObexException(r.Code, "PBAP connect");
            }
            State = BluetoothConnectionState.Connected;
        }
        catch (Exception) when (State == BluetoothConnectionState.Connecting)
        {
            State = BluetoothConnectionState.Unavailable;
            throw;
        }
    }

    public async Task<int?> GetPhonebookSizeAsync(string path = "telecom/pb.vcf", CancellationToken ct = default)
    {
        var p = new AppParameters().AddUInt16(PbapTag.MaxListCount, 0);
        var r = await Obex.GetAsync([ObexHeader.Name(path), ObexHeader.Type(PhonebookType), p.ToHeader()], ct).ConfigureAwait(false);
        if (!r.IsSuccess) throw new ObexException(r.Code, "PBAP size");
        return (int?)AppParameters.From(r.Headers)?.GetNumber(PbapTag.PhonebookSize);
    }

    /// <summary>
    /// maxCount null omits MaxListCount: BlueFerry saw failed/empty pulls on iOS when it was set.
    /// Offset 1 on pb.vcf skips the owner card (0.vcf).
    /// </summary>
    public async Task<IReadOnlyList<VCard>> PullPhonebookAsync(string path = "telecom/pb.vcf", int? maxCount = null, int offset = 0, CancellationToken ct = default)
    {
        var p = new AppParameters().AddByte(PbapTag.Format, 1);
        if (maxCount is int m) p.AddUInt16(PbapTag.MaxListCount, (ushort)Math.Clamp(m, 1, 0xFFFF));
        if (offset > 0) p.AddUInt16(PbapTag.ListStartOffset, (ushort)Math.Clamp(offset, 0, 0xFFFF));
        var r = await Obex.GetAsync([ObexHeader.Name(path), ObexHeader.Type(PhonebookType), p.ToHeader()], ct).ConfigureAwait(false);
        if (!r.IsSuccess) throw new ObexException(r.Code, "PBAP pull");
        return VCard.ParseMany(Encoding.UTF8.GetString(r.Body));
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_obex is null) return;
        try { await _obex.DisconnectAsync(ct).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or EndOfStreamException or TimeoutException or ObexProtocolException) { }
        await _obex.DisposeAsync().ConfigureAwait(false);
        _obex = null;
        State = BluetoothConnectionState.Disconnected;
    }

    private ObexClient Obex => _obex ?? throw new InvalidOperationException("PBAP client not connected");

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
