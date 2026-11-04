using System.Net;
using System.Net.Sockets;

namespace Nast.Html2Pdf.Security;

/// <summary>
/// Decides whether an IP address may be contacted. Only public unicast addresses are allowed, plus explicitly
/// configured private networks. IPv4-mapped and IPv4-embedding IPv6 forms are evaluated as the IPv4 address they carry
/// or refused.
/// </summary>
internal sealed class AddressPolicy
{
    private static readonly IPNetwork[] BlockedV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),        // "this" network
        IPNetwork.Parse("10.0.0.0/8"),       // private
        IPNetwork.Parse("100.64.0.0/10"),    // carrier-grade NAT (includes some cloud metadata endpoints)
        IPNetwork.Parse("127.0.0.0/8"),      // loopback
        IPNetwork.Parse("169.254.0.0/16"),   // link-local (cloud metadata 169.254.169.254)
        IPNetwork.Parse("172.16.0.0/12"),    // private
        IPNetwork.Parse("192.0.0.0/24"),     // IETF protocol assignments
        IPNetwork.Parse("192.0.2.0/24"),     // documentation
        IPNetwork.Parse("192.88.99.0/24"),   // 6to4 relay anycast
        IPNetwork.Parse("192.168.0.0/16"),   // private
        IPNetwork.Parse("198.18.0.0/15"),    // benchmarking
        IPNetwork.Parse("198.51.100.0/24"),  // documentation
        IPNetwork.Parse("203.0.113.0/24"),   // documentation
        IPNetwork.Parse("224.0.0.0/4"),      // multicast
        IPNetwork.Parse("240.0.0.0/4"),      // reserved and broadcast
    ];

    private static readonly IPNetwork GlobalUnicastV6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork[] BlockedV6 =
    [
        IPNetwork.Parse("2001::/23"),        // IETF protocol assignments (Teredo, benchmarking, ORCHID)
        IPNetwork.Parse("2001:db8::/32"),    // documentation
        IPNetwork.Parse("2002::/16"),        // 6to4 (embeds arbitrary IPv4)
        IPNetwork.Parse("3fff::/20"),        // documentation
    ];

    private readonly IPNetwork[] _allowedPrivate;

    public AddressPolicy(IEnumerable<string> allowedPrivateNetworks)
    {
        _allowedPrivate = allowedPrivateNetworks.Select(ParseNetwork).ToArray();
    }

    public static IPNetwork ParseNetwork(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr) || !IPNetwork.TryParse(cidr.Trim(), out var network))
        {
            throw new ArgumentException("Allowed private networks must be in CIDR notation, for example 10.20.0.0/16.");
        }

        return network;
    }

    public bool IsAllowed(IPAddress address)
    {
        var normalized = Normalize(address);
        if (_allowedPrivate.Any(n => n.Contains(normalized)))
        {
            return true;
        }

        return IsPublic(normalized);
    }

    internal static bool IsPublic(IPAddress address)
    {
        address = Normalize(address);
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !BlockedV4.Any(n => n.Contains(address));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0)
        {
            return false;
        }

        return GlobalUnicastV6.Contains(address) && !BlockedV6.Any(n => n.Contains(address));
    }

    private static IPAddress Normalize(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address;
    }
}
