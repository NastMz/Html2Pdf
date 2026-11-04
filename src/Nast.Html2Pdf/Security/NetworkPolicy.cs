using System.Net;

namespace Nast.Html2Pdf.Security;

/// <summary>Compiled, immutable form of <see cref="NetworkOptions"/>.</summary>
internal sealed class NetworkPolicy
{
    private readonly HostPattern[] _hosts;
    private readonly HashSet<int> _ports;

    public NetworkPolicy(NetworkOptions options)
    {
        _hosts = options.AllowedHosts.Select(HostPattern.Parse).ToArray();
        foreach (var port in options.AllowedPorts)
        {
            if (port is < 1 or > 65535)
            {
                throw new ArgumentException("Allowed ports must be between 1 and 65535.");
            }
        }

        _ports = new HashSet<int>(options.AllowedPorts);
        AllowHttp = options.AllowHttp;
        AllowUrlSources = options.AllowUrlSources;
        Addresses = new AddressPolicy(options.AllowedPrivateNetworks);
        if (options.MaxResponseBytes <= 0)
        {
            throw new ArgumentException("Network.MaxResponseBytes must be positive.");
        }

        MaxResponseBytes = options.MaxResponseBytes;
    }

    public bool AllowHttp { get; }

    public bool AllowUrlSources { get; }

    public AddressPolicy Addresses { get; }

    public long MaxResponseBytes { get; }

    public bool AnyHostAllowed => _hosts.Length > 0;

    /// <summary>Checks scheme, host, port and literal addresses. DNS-resolved addresses are checked at connect time.</summary>
    public string? Deny(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return "relative URL";
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!AllowHttp)
            {
                return "plain http is not allowed";
            }
        }
        else if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return $"scheme '{uri.Scheme}' is not allowed";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "URLs with embedded credentials are not allowed";
        }

        var host = HostPattern.NormalizeHost(uri);
        if (!_hosts.Any(h => h.Matches(host)))
        {
            return "host is not in the allowed host list";
        }

        if (!_ports.Contains(uri.Port))
        {
            return "port is not allowed";
        }

        if (IPAddress.TryParse(host, out var literal) && !Addresses.IsAllowed(literal))
        {
            return "address is not a permitted destination";
        }

        return null;
    }
}
