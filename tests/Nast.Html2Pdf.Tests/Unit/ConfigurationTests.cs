using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Rendering;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class ConfigurationTests
{
    public static TheoryData<string, Action<Html2PdfOptions>> InvalidOptions => new()
    {
        { "timeout", o => o.DefaultTimeout = TimeSpan.Zero },
        { "attempts", o => o.MaxAttempts = 0 },
        { "concurrency", o => o.Capacity.MaxConcurrentConversions = 0 },
        { "queue", o => o.Capacity.MaxQueueLength = -1 },
        { "queue timeout", o => o.Capacity.QueueTimeout = TimeSpan.FromSeconds(-1) },
        { "host", o => o.Network.AllowedHosts.Add("http://bad") },
        { "port", o => o.Network.AllowedPorts.Add(70000) },
        { "cidr", o => o.Network.AllowedPrivateNetworks.Add("10.0.0.0/99") },
        { "response size", o => o.Network.MaxResponseBytes = 0 },
        { "root", o => o.LocalFiles.AllowedRoots.Add("not/absolute") },
        { "launch", o => o.Browser.LaunchTimeout = TimeSpan.Zero },
        { "recycle", o => o.Browser.MaxConversionsPerBrowser = 0 },
        { "temp", o => o.Browser.TemporaryDirectory = "relative" },
        { "steps", o => o.Templates.MaxSteps = 0 },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Invalid_configuration_fails_at_construction(string name, Action<Html2PdfOptions> configure)
    {
        var options = TestEnvironment.Options(configure);
        Assert.ThrowsAny<ArgumentException>(() => new Html2PdfConverter(options));
        Assert.NotNull(name);
    }

    [Fact]
    public void Defaults_are_secure_and_bounded()
    {
        var options = new Html2PdfOptions();
        Assert.False(options.AllowScripts);
        Assert.False(options.Network.AllowUrlSources);
        Assert.Empty(options.Network.AllowedHosts);
        Assert.False(options.Network.AllowHttp);
        Assert.Empty(options.Network.AllowedPrivateNetworks);
        Assert.Empty(options.LocalFiles.AllowedRoots);
        Assert.False(options.Browser.DisableSandbox);
        Assert.InRange(options.Capacity.MaxConcurrentConversions, 1, 4);
        Assert.Equal(32, options.Capacity.MaxQueueLength);
        Assert.Equal(TimeSpan.FromSeconds(60), options.DefaultTimeout);
        var conversion = new PdfConversionOptions();
        Assert.Equal(PaperSize.A4, conversion.PaperSize);
        Assert.Equal(PageOrientation.Portrait, conversion.Orientation);
        Assert.Equal(PageLayoutPrecedence.Job, conversion.LayoutPrecedence);
        Assert.Equal(ResourceFailurePolicy.Fail, conversion.ResourceFailures);
    }

    [Fact]
    public void Launch_arguments_include_the_network_backstop_and_respect_the_sandbox_setting()
    {
        var args = BrowserHost.LaunchArguments(disableSandbox: false);
        Assert.Contains(args, a => a.StartsWith("--proxy-server=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.Contains("MAP * ~NOTFOUND", StringComparison.Ordinal));
        Assert.DoesNotContain("--no-sandbox", args);
        Assert.Contains("--no-sandbox", BrowserHost.LaunchArguments(disableSandbox: true));
    }

    [Theory]
    [InlineData("Chrome/153.0.8010.12", 153)]
    [InlineData("HeadlessChrome/120.0.6099.0", 120)]
    [InlineData("garbage", 0)]
    public void Browser_versions_are_parsed(string version, int major) => Assert.Equal(major, BrowserHost.ParseMajorVersion(version));

    [Fact]
    public async Task Dependency_injection_registers_a_singleton_owned_by_the_container()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHtml2Pdf(o => o.Capacity.MaxConcurrentConversions = 2);
        Html2PdfConverter converter;
        await using (var provider = services.BuildServiceProvider())
        {
            converter = provider.GetRequiredService<Html2PdfConverter>();
            Assert.Same(converter, provider.GetRequiredService<IHtml2PdfConverter>());
            Assert.Equal(2, converter.GetHealth().MaxConcurrentConversions);
            Assert.Equal(Html2PdfStatus.Idle, converter.GetHealth().Status);
        }

        Assert.Equal(Html2PdfStatus.Stopped, converter.GetHealth().Status);
    }

    [Fact]
    public void Options_are_copied_at_construction()
    {
        var options = TestEnvironment.Options();
        using var converter = new Html2PdfConverter(options);
        options.Capacity.MaxConcurrentConversions = 99;
        Assert.NotEqual(99, converter.GetHealth().MaxConcurrentConversions);
    }
}
