using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PhoneBridge.Server;

public static class NetworkRules
{
    /// <summary>Loopback, RFC 1918, link-local, IPv6 ULA/link-local. Everything else is refused (SPEC §9).</summary>
    public static bool IsPrivate(IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
        return false;
    }

    /// <summary>Best guess of the LAN IPv4 the iPhone can reach: an up, non-virtual interface with a gateway.</summary>
    public static IPAddress? PrimaryLanAddress()
    {
        var candidates =
            from nic in NetworkInterface.GetAllNetworkInterfaces()
            where nic.OperationalStatus == OperationalStatus.Up
                  && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            let props = nic.GetIPProperties()
            from addr in props.UnicastAddresses
            where addr.Address.AddressFamily == AddressFamily.InterNetwork
                  && IsPrivate(addr.Address) && !IPAddress.IsLoopback(addr.Address)
                  && !addr.Address.ToString().StartsWith("169.254.")
            orderby props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork) descending,
                    nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 descending
            select addr.Address;
        return candidates.FirstOrDefault();
    }
}
