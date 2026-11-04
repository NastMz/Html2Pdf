using System.Globalization;
using System.Net;

namespace Nast.Html2Pdf.Security;

/// <summary>An allowed-host entry: an exact host, a <c>*.</c> subdomain wildcard or an IP literal.</summary>
internal sealed class HostPattern
{
    private static readonly IdnMapping Idn = new();
    private readonly string _host;
    private readonly bool _subdomains;

    private HostPattern(string host, bool subdomains)
    {
        _host = host;
        _subdomains = subdomains;
    }

    public static HostPattern Parse(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Allowed hosts cannot be empty.");
        }

        var text = pattern.Trim();
        var subdomains = text.StartsWith("*.", StringComparison.Ordinal);
        if (subdomains)
        {
            text = text[2..];
        }

        if (text.Contains('*') || text.Contains('/') || text.Contains(':') && !IPAddress.TryParse(text.Trim('[', ']'), out _))
        {
            throw new ArgumentException($"Invalid allowed host '{pattern}'. Use a host name, '*.domain' or an IP address, without scheme, port or path.");
        }

        if (IPAddress.TryParse(text.Trim('[', ']'), out var ip))
        {
            if (subdomains)
            {
                throw new ArgumentException($"Invalid allowed host '{pattern}': wildcards cannot be combined with IP addresses.");
            }

            return new HostPattern(ip.ToString(), false);
        }

        string ascii;
        try
        {
            ascii = Idn.GetAscii(text.TrimEnd('.')).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"Invalid allowed host '{pattern}'.");
        }

        if (ascii.Length == 0)
        {
            throw new ArgumentException($"Invalid allowed host '{pattern}'.");
        }

        return new HostPattern(ascii, subdomains);
    }

    /// <summary>Matches a normalized host (<see cref="NormalizeHost"/>).</summary>
    public bool Matches(string normalizedHost)
    {
        if (_subdomains)
        {
            return normalizedHost.Length > _host.Length + 1
                && normalizedHost.EndsWith("." + _host, StringComparison.Ordinal);
        }

        return string.Equals(normalizedHost, _host, StringComparison.Ordinal);
    }

    /// <summary>Lower-case ASCII (punycode) host, without trailing dot or IPv6 brackets.</summary>
    public static string NormalizeHost(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        return IPAddress.TryParse(host, out var ip) ? ip.ToString() : host;
    }
}
