using System.Diagnostics;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>Saturation, expiry and mixed failures under parallel load, observed from outside the library.</summary>
public sealed class ConcurrencyTests : IDisposable
{
    private readonly TestServer _server = new();

    public ConcurrencyTests()
    {
        _server.MapDelayed("/hold.svg", TimeSpan.FromMilliseconds(1500));
        _server.MapDelayed("/long.svg", TimeSpan.FromSeconds(4));
    }

    private Html2PdfConverter Create(Action<Html2PdfOptions> configure) => TestEnvironment.Create(o =>
    {
        o.Network.AllowedHosts.Add("127.0.0.1");
        o.Network.AllowHttp = true;
        o.Network.AllowedPorts.Add(_server.Port);
        o.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
        configure(o);
    });

    private PdfSource Holding(string path, int i) => PdfSource.FromHtml($"<p>job-{i}</p><img src=\"{_server.BaseUrl}{path}?{i}\">");

    [Fact]
    public async Task Saturation_admits_exactly_the_configured_capacity_and_never_exceeds_concurrency()
    {
        await using var converter = Create(o =>
        {
            o.Capacity.MaxConcurrentConversions = 2;
            o.Capacity.MaxQueueLength = 3;
        });
        await converter.WarmUpAsync();

        var watch = Stopwatch.StartNew();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            try
            {
                var result = await converter.ConvertAsync(Holding("/hold.svg", i));
                using var pdf = new PdfInspector(result.Content);
                Assert.Contains($"job-{i}", pdf.AllText);
                return "ok";
            }
            catch (PdfConversionException ex)
            {
                Assert.Equal(PdfConversionError.CapacityExceeded, ex.Error);
                Assert.True(ex.IsTransient);
                return watch.Elapsed < TimeSpan.FromSeconds(1) ? "rejected-immediately" : "rejected-late";
            }
        }));

        Assert.Equal(5, outcomes.Count(o => o == "ok"));
        Assert.Equal(5, outcomes.Count(o => o == "rejected-immediately"));
        Assert.True(_server.MaxConcurrent <= 2, $"server saw {_server.MaxConcurrent} concurrent document loads");
        Assert.Equal(2, _server.MaxConcurrent);
        Assert.Equal(0, converter.GetHealth().ActiveConversions);
        Assert.Equal(0, converter.GetHealth().QueuedConversions);
    }

    [Fact]
    public async Task Expired_queue_entries_fail_and_do_not_leak_or_duplicate_capacity()
    {
        await using var converter = Create(o =>
        {
            o.Capacity.MaxConcurrentConversions = 2;
            o.Capacity.QueueTimeout = TimeSpan.FromMilliseconds(400);
        });
        await converter.WarmUpAsync();

        var first = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try
            {
                await converter.ConvertAsync(Holding("/long.svg", i));
                return "ok";
            }
            catch (PdfConversionException ex) when (ex.Error == PdfConversionError.CapacityExceeded)
            {
                return "expired";
            }
        }));
        Assert.Equal(2, first.Count(o => o == "ok"));
        Assert.Equal(4, first.Count(o => o == "expired"));
        Assert.Equal(0, converter.GetHealth().ActiveConversions);
        Assert.Equal(0, converter.GetHealth().QueuedConversions);

        // Exactly two slots remain: two conversions are admitted at once and run concurrently.
        _server.MapDelayed("/probe.svg", TimeSpan.FromSeconds(1));
        _server.ResetMaxConcurrent();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => converter.ConvertAsync(Holding("/probe.svg", 100 + i))));
        Assert.All(results, r => Assert.True(r.ByteCount > 0));
        Assert.Equal(2, _server.MaxConcurrent);
        Assert.Equal(0, converter.GetHealth().ActiveConversions);
    }

    [Fact]
    public async Task Mixed_failures_in_parallel_produce_the_right_outcome_for_every_job()
    {
        await using var converter = Create(o =>
        {
            o.Capacity.MaxConcurrentConversions = 4;
            o.Capacity.MaxQueueLength = 64;
        });

        var jobs = Enumerable.Range(0, 36).Select(i => (i % 6) switch
        {
            0 => (Expected: (PdfConversionError?)null, Run: (Func<Task<PdfDocument>>)(() => converter.ConvertAsync(PdfSource.FromHtml($"<p>ok-{i}</p>")))),
            1 => (PdfConversionError.AccessDenied, () => converter.ConvertAsync(PdfSource.FromHtml("<img src=\"https://denied.example/x.png\">"))),
            2 => (PdfConversionError.TemplateError, () => converter.ConvertAsync(PdfSource.FromTemplate("{{ Missing }}", new { }))),
            3 => (PdfConversionError.ReadinessNotMet, () => converter.ConvertAsync(PdfSource.FromHtml("<p>x</p>"), new PdfConversionOptions { Timeout = TimeSpan.FromSeconds(3), Readiness = { WaitForSelector = "#nope" } })),
            4 => (PdfConversionError.InvalidRequest, () => converter.ConvertAsync(PdfSource.FromHtml("<p>x</p>"), new PdfConversionOptions { Scale = 9 })),
            _ => (null, () => converter.ConvertAsync(Holding("/hold.svg", i))),
        }).ToArray();

        var outcomes = await Task.WhenAll(jobs.Select(async (job, index) =>
        {
            try
            {
                var result = await job.Run();
                using var pdf = new PdfInspector(result.Content);
                return (index, (PdfConversionError?)null, pdf.AllText);
            }
            catch (PdfConversionException ex)
            {
                return (index, ex.Error, ex.Message);
            }
        }));

        foreach (var (index, error, text) in outcomes)
        {
            if (jobs[index].Expected == PdfConversionError.ReadinessNotMet && error == PdfConversionError.Timeout)
            {
                // Its 3 s deadline can legitimately expire while it waits behind other jobs; the error says so.
                Assert.Contains("queue stage", text);
                continue;
            }

            Assert.Equal(jobs[index].Expected, error);
            if (error is null)
            {
                Assert.Contains(index % 6 == 0 ? $"ok-{index}" : $"job-{index}", text);
            }
        }

        Assert.Equal(0, converter.GetHealth().ActiveConversions);
        Assert.Equal(12, converter.GetHealth().SucceededConversions);
        Assert.Equal(24, converter.GetHealth().FailedConversions);
    }

    public void Dispose() => _server.Dispose();
}
