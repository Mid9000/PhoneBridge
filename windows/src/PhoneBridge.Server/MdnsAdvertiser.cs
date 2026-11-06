using System.Net;
using System.Runtime.InteropServices;

namespace PhoneBridge.Server;

/// <summary>
/// Advertises _phonebridge._tcp.local through the Windows built-in mDNS responder (DnsServiceRegister, Windows 10 1903+).
/// No NuGet and no fight with the system service over UDP 5353.
/// </summary>
public sealed class MdnsAdvertiser : IDisposable
{
    public const string ServiceType = "_phonebridge._tcp.local";

    IntPtr _instance, _request, _cancel;
    RegisterComplete? _callback;   // kept alive while Windows may call it

    public int LastStatus { get; private set; } = -1;

    /// <summary>Raised from a Windows thread with the final registration status (0 = registered).</summary>
    public event Action<int>? Completed;

    /// <returns>false when the OS does not support it or the call fails (the QR code still carries the IP).</returns>
    public bool Start(string instanceName, ushort port, IPAddress? ipv4, IReadOnlyDictionary<string, string> txt)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)) return false;
        Stop();

        var keys = txt.Keys.ToArray();
        var values = keys.Select(k => txt[k]).ToArray();
        var ipPtr = IntPtr.Zero;
        try
        {
            if (ipv4 is not null)
            {
                ipPtr = Marshal.AllocHGlobal(4);
                Marshal.WriteInt32(ipPtr, BitConverter.ToInt32(ipv4.GetAddressBytes()));   // IP4_ADDRESS: network byte order in memory
            }
            var host = Environment.MachineName.ToLowerInvariant() + ".local";
            _instance = DnsServiceConstructInstance($"{instanceName}.{ServiceType}", host, ipPtr, IntPtr.Zero,
                port, 0, 0, (uint)keys.Length, keys, values);
        }
        finally
        {
            if (ipPtr != IntPtr.Zero) Marshal.FreeHGlobal(ipPtr);   // copied by DnsServiceConstructInstance
        }
        if (_instance == IntPtr.Zero) return false;

        _callback = (status, _, instance) =>
        {
            LastStatus = (int)status;
            if (instance != IntPtr.Zero) DnsServiceFreeInstance(instance);
            Completed?.Invoke((int)status);
        };
        var request = new RegisterRequest
        {
            Version = 1, // DNS_QUERY_REQUEST_VERSION1
            InterfaceIndex = 0,
            ServiceInstance = _instance,
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<RegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);
        _cancel = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(_cancel, IntPtr.Zero);

        const uint DnsRequestPending = 9506;
        var result = DnsServiceRegister(_request, _cancel);
        LastStatus = (int)result;
        if (result == DnsRequestPending) return true;
        (_request, _cancel, _instance) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return false;
    }

    public void Stop()
    {
        // shortcut: request/instance memory is not freed because deregistration completes asynchronously and may
        // still read it; a few hundred bytes per stop (shutdown or network change). Free from the callback if Stop becomes frequent.
        if (_request != IntPtr.Zero) DnsServiceDeRegister(_request, IntPtr.Zero);
        (_request, _cancel, _instance) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Stop();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate void RegisterComplete(uint status, IntPtr context, IntPtr instance);

    [StructLayout(LayoutKind.Sequential)]
    struct RegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr ServiceInstance;
        public IntPtr Callback;
        public IntPtr QueryContext;
        public IntPtr Credentials;
        public int UnicastEnabled;
    }

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr DnsServiceConstructInstance(string serviceName, string hostName, IntPtr ip4, IntPtr ip6,
        ushort port, ushort priority, ushort weight, uint propertiesCount, string[] keys, string[] values);

    [DllImport("dnsapi.dll")] static extern void DnsServiceFreeInstance(IntPtr instance);
    [DllImport("dnsapi.dll")] static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);
    [DllImport("dnsapi.dll")] static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);
}
