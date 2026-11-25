using System.Net;
using System.Net.Sockets;

namespace Condor2Nmea.Core.Network;

/// <summary>Kind of network adapter, as far as it matters for reaching the PC from a phone in the WLAN.</summary>
public enum AdapterKind
{
    Wlan,
    Ethernet,
    Other,
    /// <summary>VPN or virtual adapter (Hamachi, Radmin, ZeroTier, Tailscale, Hyper-V, WSL, VirtualBox, VMware …).</summary>
    Virtual,
}

/// <summary>One IPv4 address of a network adapter that is up.</summary>
public sealed record AdapterAddress(string AdapterName, string Description, AdapterKind Kind, bool HasGateway, IPAddress Address)
{
    public string Label => Kind switch
    {
        AdapterKind.Wlan => $"WLAN ({AdapterName})",
        AdapterKind.Ethernet => $"LAN-Kabel ({AdapterName})",
        AdapterKind.Virtual => $"VPN/virtuell, für das Handy meist falsch ({Description})",
        _ => AdapterName,
    };
}

/// <summary>
/// Picks the address a phone in the same WLAN can reach. VPN tools such as Hamachi bring their own adapter
/// with an address (25.x.x.x) that the phone cannot reach, so virtual adapters are recognized and put last.
/// </summary>
public static class LocalAddressRanker
{
    private static readonly string[] VirtualMarkers =
    {
        "hamachi", "radmin", "zerotier", "tailscale", "wireguard", "openvpn", "vpn", "tap-windows", "tap adapter", "wintun",
        "virtual", "vethernet", "hyper-v", "wsl", "virtualbox", "vmware", "vmnet", "npcap", "loopback", "bluetooth",
        "docker", "nordlynx", "fortinet", "cisco anyconnect", "globalprotect",
    };

    public static bool LooksVirtual(string name, string description) =>
        VirtualMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                description.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for 10/8, 172.16/12 and 192.168/16, the ranges home and club WLANs use.</summary>
    public static bool IsPrivateLan(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Hamachi (25/8), Radmin (26/8) and CGNAT/Tailscale (100.64/10) addresses are never a WLAN address.</summary>
    public static bool IsKnownVpnRange(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] is 25 or 26 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
    }

    /// <summary>Usable IPv4 addresses, best first: WLAN/LAN with gateway and private address, then the rest, virtual last.</summary>
    public static IReadOnlyList<AdapterAddress> Rank(IEnumerable<AdapterAddress> addresses) =>
        addresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) &&
                        !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => a.Kind != AdapterKind.Virtual && (LooksVirtual(a.AdapterName, a.Description) || IsKnownVpnRange(a.Address))
                ? a with { Kind = AdapterKind.Virtual }
                : a)
            .GroupBy(a => a.Address)
            .Select(g => g.First())
            .OrderBy(Score)
            .ToList();

    private static int Score(AdapterAddress a)
    {
        if (a.Kind == AdapterKind.Virtual)
            return 100;
        var score = a.Kind switch { AdapterKind.Wlan => 0, AdapterKind.Ethernet => 1, _ => 5 };
        if (!a.HasGateway)
            score += 10;
        if (!IsPrivateLan(a.Address))
            score += 20;
        return score;
    }
}
