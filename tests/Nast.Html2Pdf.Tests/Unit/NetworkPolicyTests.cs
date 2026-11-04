using System.Net;
using Nast.Html2Pdf.Security;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class NetworkPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.5.4")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("100.100.100.200")] // carrier-grade NAT range (Alibaba metadata)
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.10")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00:ec2::254")] // AWS IPv6 metadata
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")] // NAT64 embedding 127.0.0.1
    [InlineData("2002:7f00:1::")] // 6to4 embedding 127.0.0.1
    [InlineData("2001:db8::1")]
    public void Non_public_addresses_are_refused(string address)
        => Assert.False(new AddressPolicy([]).IsAllowed(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700::6810:84e5")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string address)
        => Assert.True(new AddressPolicy([]).IsAllowed(IPAddress.Parse(address)));

    [Fact]
    public void Configured_private_networks_are_allowed_and_nothing_else()
    {
        var policy = new AddressPolicy(["10.20.0.0/16"]);
        Assert.True(policy.IsAllowed(IPAddress.Parse("10.20.3.4")));
        Assert.True(policy.IsAllowed(IPAddress.Parse("::ffff:10.20.3.4")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("10.21.0.1")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("127.0.0.1")));
        Assert.Throws<ArgumentException>(() => new AddressPolicy(["not-a-cidr"]));
    }

    private static NetworkPolicy Policy(Action<NetworkOptions> configure)
    {
        var options = new NetworkOptions();
        configure(options);
        return new NetworkPolicy(options);
    }

    [Fact]
    public void Nothing_is_allowed_by_default()
    {
        var policy = new NetworkPolicy(new NetworkOptions());
        Assert.False(policy.AnyHostAllowed);
        Assert.False(policy.AllowUrlSources);
        Assert.NotNull(policy.Deny(new Uri("https://example.com/")));
    }

    [Theory]
    [InlineData("https://example.com/a", null)]
    [InlineData("https://EXAMPLE.com./a", null)]
    [InlineData("https://cdn.example.org/x", null)]
    [InlineData("https://a.b.example.org/x", null)]
    [InlineData("https://example.org/x", "host")]
    [InlineData("https://evil-example.com/", "host")]
    [InlineData("https://example.com.evil.net/", "host")]
    [InlineData("http://example.com/", "http")]
    [InlineData("https://example.com:8443/", "port")]
    [InlineData("https://user:pass@example.com/", "credentials")]
    [InlineData("ftp://example.com/", "scheme")]
    [InlineData("file:///etc/passwd", "scheme")]
    [InlineData("https://127.0.0.1/", "host")]
    public void Urls_are_checked_against_scheme_host_port_and_credentials(string url, string? expectedDenial)
    {
        var policy = Policy(o =>
        {
            o.AllowedHosts.Add("example.com");
            o.AllowedHosts.Add("*.example.org");
        });
        var denial = policy.Deny(new Uri(url));
        if (expectedDenial is null)
        {
            Assert.Null(denial);
        }
        else
        {
            Assert.NotNull(denial);
            Assert.Contains(expectedDenial, denial, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Allowed_ip_literals_are_still_subject_to_address_rules()
    {
        var policy = Policy(o =>
        {
            o.AllowedHosts.Add("127.0.0.1");
            o.AllowedHosts.Add("::1");
            o.AllowHttp = true;
        });
        Assert.Contains("address", policy.Deny(new Uri("http://127.0.0.1/"))!);
        Assert.Contains("address", policy.Deny(new Uri("http://[::1]/"))!);
        // Alternative IPv4 spellings are normalized by the URI parser before matching.
        Assert.Contains("address", policy.Deny(new Uri("http://2130706433/"))!);
    }

    [Fact]
    public void Internationalized_hosts_are_compared_in_ascii_form()
    {
        var policy = Policy(o => o.AllowedHosts.Add("bücher.example"));
        Assert.Null(policy.Deny(new Uri("https://xn--bcher-kva.example/")));
        Assert.Null(policy.Deny(new Uri("https://BÜCHER.example/")));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com:443")]
    [InlineData("*.127.0.0.1")]
    [InlineData("")]
    public void Invalid_host_patterns_are_rejected(string pattern)
        => Assert.Throws<ArgumentException>(() => Policy(o => o.AllowedHosts.Add(pattern)));

    [Theory]
    [InlineData("https://cdn.example.com/a/b/c/logo.png?sig=SECRET#frag", "https://cdn.example.com/…/logo.png")]
    [InlineData("https://user:pw@example.com/?token=SECRET", "https://example.com/")]
    [InlineData("http://example.com:8080/x", "http://example.com:8080/x")]
    [InlineData("data:image/png;base64,AAAA", "data:image/png")]
    [InlineData("http://r0.files.html2pdf.invalid/images/a%20b.png", "images/a b.png")]
    public void Redaction_removes_secrets_and_host_paths(string url, string expected)
        => Assert.Equal(expected, Redaction.Url(url));
}
