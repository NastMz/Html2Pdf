using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>
/// Sustained mixed workload. Run separately: <c>dotnet test --filter Category=Soak</c>. Iterations can be raised with
/// HTML2PDF_SOAK_ITERATIONS; samples are written to HTML2PDF_SOAK_REPORT (JSON) when set.
/// </summary>
[Trait("Category", "Soak")]
public sealed class SoakTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly TestServer _server = new();

    [Fact]
    public async Task Sustained_mixed_workload_has_bounded_processes_memory_temp_files_and_state()
    {
        var iterations = int.TryParse(Environment.GetEnvironmentVariable("HTML2PDF_SOAK_ITERATIONS"), out var n) ? n : 400;
        _root.File("assets/red.png", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "red.png")));
        _root.File("doc.html", "<link rel=stylesheet href=style.css><h1>File</h1><img src=assets/red.png>");
        _root.File("style.css", "h1 { color: #0000ff }");
        _server.MapText("/remote.svg", "image/svg+xml", Svg.BlueSquare);

        var options = TestEnvironment.Options(o =>
        {
            o.LocalFiles.AllowedRoots.Add(_root.Path);
            o.Network.AllowedHosts.Add("127.0.0.1");
            o.Network.AllowHttp = true;
            o.Network.AllowedPorts.Add(_server.Port);
            o.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
            o.Capacity.MaxConcurrentConversions = 4;
            o.Capacity.MaxQueueLength = 16;
            o.Browser.MaxConversionsPerBrowser = 100;
        });
        var profiles = options.Browser.TemporaryDirectory!;
        var samples = new List<Sample>();
        var outcomes = new Dictionary<string, int>();
        var watch = Stopwatch.StartNew();

        var logs = new CapturingLoggerProvider(Microsoft.Extensions.Logging.LogLevel.Trace);
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace));
        await using (var converter = new Html2PdfConverter(options, loggerFactory))
        {
            var sources = new Func<int, (PdfSource Source, PdfConversionOptions? Options)>[]
            {
                i => (PdfSource.FromTemplate(Documents.InvoiceTemplate, Documents.InvoiceModel($"Customer {i}")), null),
                i => (PdfSource.FromHtml(Documents.Report(60 + (i % 40))), new PdfConversionOptions { Margins = new PageMargins("20mm"), Footer = new PdfHeaderFooter("{{ pdf.page_number }}/{{ pdf.total_pages }}", "8mm") }),
                i => (PdfSource.FromFile(Path.Combine(_root.Path, "doc.html")), null),
                i => (PdfSource.FromHtml($"<img src=\"{_server.BaseUrl}/remote.svg?{i}\">"), null),
                i => (PdfSource.FromHtml("<img src=\"https://denied.example/x.png\">"), null),
                i => (PdfSource.FromTemplate("{{ Missing }}", null), null),
                i => (PdfSource.FromHtml("<p>x</p>"), new PdfConversionOptions { Timeout = TimeSpan.FromSeconds(1), Readiness = { WaitForSelector = "#never" } }),
            };

            using var gate = new SemaphoreSlim(8);
            var tasks = new List<Task>();
            for (var i = 0; i < iterations; i++)
            {
                await gate.WaitAsync();
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var (source, conversionOptions) = sources[index % sources.Length](index);
                        string outcome;
                        try
                        {
                            var result = await converter.ConvertAsync(source, conversionOptions);
                            outcome = result.ByteCount > 0 ? "success" : "empty";
                        }
                        catch (PdfConversionException ex)
                        {
                            outcome = ex.Error.ToString();
                        }

                        lock (outcomes)
                        {
                            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));

                if (i % 50 == 49)
                {
                    samples.Add(Sample.Take(i + 1, watch.Elapsed, profiles, converter));
                }
            }

            await Task.WhenAll(tasks);
            samples.Add(Sample.Take(iterations, watch.Elapsed, profiles, converter));
            Assert.Equal(0, converter.GetHealth().ActiveConversions);
            Assert.Equal(0, converter.GetHealth().QueuedConversions);
            Assert.Equal(0, converter.OperationsInProgress);
            Assert.Equal(iterations, converter.GetHealth().SucceededConversions + converter.GetHealth().FailedConversions);
        }

        var afterShutdown = Sample.Take(iterations, watch.Elapsed, profiles, null);
        foreach (var sample in samples)
        {
            output.WriteLine(sample.ToString());
        }

        foreach (var entry in logs.Entries.Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning && !e.Message.Contains("AccessDenied") && !e.Message.Contains("TemplateError") && !e.Message.Contains("ReadinessNotMet") && !e.Message.Contains("Timeout")))
        {
            output.WriteLine($"LOG {entry.Level}: {entry.Message.Split((char)10)[0]}");
        }

        var failedIds = logs.Entries.Where(e => e.Message.Contains("RenderingFailed") || e.Message.Contains("retrying")).Select(e => e.Message.Split(' ')[1].TrimEnd(':')).ToHashSet();
        foreach (var entry in logs.Entries.Where(e => failedIds.Any(id => e.Message.Contains(id))))
        {
            output.WriteLine($"DETAIL {entry.Level}: {entry.Message.Replace("\n", " || ")[..Math.Min(entry.Message.Length, 3000)]}");
        }

        output.WriteLine("Outcomes: " + string.Join(", ", outcomes.Select(o => $"{o.Key}={o.Value}")));
        if (Environment.GetEnvironmentVariable("HTML2PDF_SOAK_REPORT") is { Length: > 0 } reportPath)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { iterations, outcomes, samples, afterShutdown }, ReportJson));
        }

        // Expected outcome mix: every category behaves as designed throughout the run.
        var perKind = iterations / 7;
        Assert.True(outcomes.GetValueOrDefault("success") >= perKind * 4, "success count");
        Assert.True(outcomes.GetValueOrDefault(nameof(PdfConversionError.AccessDenied)) >= perKind);
        Assert.True(outcomes.GetValueOrDefault(nameof(PdfConversionError.TemplateError)) >= perKind);
        // The readiness job has a 1 s deadline that includes queueing, so it ends as ReadinessNotMet or Timeout.
        Assert.True(outcomes.GetValueOrDefault(nameof(PdfConversionError.ReadinessNotMet)) + outcomes.GetValueOrDefault(nameof(PdfConversionError.Timeout)) >= perKind);
        Assert.Equal(0, outcomes.GetValueOrDefault(nameof(PdfConversionError.RenderingFailed)));
        Assert.Equal(0, outcomes.GetValueOrDefault(nameof(PdfConversionError.CapacityExceeded)));

        // Bounded resources: browser processes never accumulate across recycles, memory does not trend upwards,
        // and at most the current plus retiring browser profiles exist at any time.
        Assert.All(samples, s => Assert.InRange(s.BrowserProcesses, 0, 40));
        Assert.All(samples, s => Assert.InRange(s.ProfileDirectories, 0, 2));
        if (ProcessProbe.Supported && samples.Count >= 6)
        {
            var early = samples.Skip(1).Take(samples.Count / 3).Average(s => s.BrowserResidentMb);
            var late = samples.Skip(samples.Count * 2 / 3).Average(s => s.BrowserResidentMb);
            Assert.True(late < early * 1.5 + 150, $"browser memory grew from {early:0} MB to {late:0} MB");
        }

        var managedEarly = samples[1].ManagedMb;
        Assert.True(samples[^1].ManagedMb < managedEarly + 64, $"managed memory grew from {managedEarly:0} MB to {samples[^1].ManagedMb:0} MB");
        Assert.Equal(0, await ProcessProbe.WaitForNoProcessesAsync(profiles, TimeSpan.FromSeconds(10)));
        await ReliabilityTests.AssertNoLeftoversAsync(options);
    }

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public void Dispose()
    {
        _server.Dispose();
        _root.Dispose();
    }

    private sealed record Sample(int Completed, double ElapsedSeconds, int BrowserProcesses, double BrowserResidentMb, int ProfileDirectories, double ManagedMb, int Active, int Queued)
    {
        public static Sample Take(int completed, TimeSpan elapsed, string profiles, Html2PdfConverter? converter)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var profileRoot = Path.Combine(profiles, "nast-html2pdf");
            return new Sample(
                completed,
                Math.Round(elapsed.TotalSeconds, 1),
                ProcessProbe.CountProcessesUsing(profiles),
                Math.Round(ProcessProbe.ResidentBytesUsing(profiles) / 1048576.0, 1),
                Directory.Exists(profileRoot) ? Directory.EnumerateDirectories(profileRoot).Count() : 0,
                Math.Round(GC.GetTotalMemory(true) / 1048576.0, 1),
                converter?.GetHealth().ActiveConversions ?? 0,
                converter?.GetHealth().QueuedConversions ?? 0);
        }
    }
}
