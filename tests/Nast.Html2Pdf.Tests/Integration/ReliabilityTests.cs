using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>Interruption and failure at every stage of the lifecycle, with resource recovery checks.</summary>
public sealed class ReliabilityTests : IDisposable
{
    private readonly TestServer _server = new();
    private readonly TempDirectory _output = new();

    public ReliabilityTests()
    {
        _server.MapDelayed("/slow.svg", TimeSpan.FromSeconds(20));
        _server.MapDelayed("/brief.svg", TimeSpan.FromMilliseconds(1500));
    }

    private Html2PdfConverter Create(Action<Html2PdfOptions>? configure = null) => TestEnvironment.Create(o =>
    {
        o.Network.AllowedHosts.Add("127.0.0.1");
        o.Network.AllowHttp = true;
        o.Network.AllowedPorts.Add(_server.Port);
        o.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
        configure?.Invoke(o);
    });

    private PdfSource Slow => PdfSource.FromHtml($"<p>slow</p><img src=\"{_server.BaseUrl}/slow.svg\">");

    private PdfSource Brief(string tag = "brief") => PdfSource.FromHtml($"<p>{tag}</p><img src=\"{_server.BaseUrl}/brief.svg?{tag}\">");

    private static readonly PdfSource Quick = PdfSource.FromHtml("<p>quick</p>");

    private static async Task AssertRecoveredAsync(Html2PdfConverter converter)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (converter.GetHealth().ActiveConversions + converter.GetHealth().QueuedConversions > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        var health = converter.GetHealth();
        Assert.Equal(0, health.ActiveConversions);
        Assert.Equal(0, health.QueuedConversions);
        Assert.Equal(0, converter.OperationsInProgress);
        (await converter.ConvertAsync(Quick)).Content.ToArray();
    }

    [Fact]
    public async Task Cancellation_before_admission_starts_nothing()
    {
        await using var converter = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(Quick, cancellationToken: cts.Token));
        Assert.False(converter.GetHealth().BrowserRunning);
        Assert.Equal(1, converter.GetHealth().FailedConversions);
    }

    [Fact]
    public async Task Cancellation_while_queued_during_preparation_and_while_rendering_is_prompt_and_releases_capacity()
    {
        await using var converter = Create(o => o.Capacity.MaxConcurrentConversions = 1);

        // While rendering: blocked on a resource that takes 20 s.
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500)))
        {
            var watch = Stopwatch.StartNew();
            var rendering = converter.ConvertAsync(Slow, cancellationToken: cts.Token);

            // While queued behind it.
            using var queuedCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(Quick, cancellationToken: queuedCts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rendering);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"cancellation took {watch.Elapsed}");
        }

        // During preparation.
        using (var cts = new CancellationTokenSource())
        {
            converter.AfterAdmission = _ =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(Quick, cancellationToken: cts.Token));
            converter.AfterAdmission = null;
        }

        await AssertRecoveredAsync(converter);
    }

    [Fact]
    public async Task Deadlines_apply_to_rendering_and_readiness_with_distinct_errors()
    {
        await using var converter = Create();
        await converter.WarmUpAsync(); // the deadline below must expire while loading, not while the browser starts
        var watch = Stopwatch.StartNew();
        var timeout = await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertAsync(Slow, new PdfConversionOptions { Timeout = TimeSpan.FromSeconds(8) }));
        Assert.Equal(PdfConversionError.Timeout, timeout.Error);
        Assert.Contains("document loading", timeout.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(18), $"took {watch.Elapsed}");

        var readiness = await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertAsync(Quick, new PdfConversionOptions
        {
            Timeout = TimeSpan.FromSeconds(10),
            Readiness = { WaitForSelector = ".never" },
        }));
        Assert.Equal(PdfConversionError.ReadinessNotMet, readiness.Error);
        Assert.False(readiness.IsTransient && readiness.Error == PdfConversionError.ReadinessNotMet);

        // A satisfied readiness condition does not delay success.
        await converter.ConvertAsync(PdfSource.FromHtml("<p class=\"ready\">ok</p>"), new PdfConversionOptions { Readiness = { WaitForSelector = ".ready" } });
        await AssertRecoveredAsync(converter);
    }

    [Fact]
    public async Task File_output_never_exposes_partial_files_or_destroys_existing_ones()
    {
        await using var converter = Create();
        var existing = _output.File("existing.pdf", "PREVIOUS");

        var refused = await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertToFileAsync(Quick, existing));
        Assert.Equal(PdfConversionError.OutputFailed, refused.Error);
        Assert.Equal("PREVIOUS", await File.ReadAllTextAsync(existing));

        // Rendering failure: existing file untouched.
        await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertToFileAsync(PdfSource.FromHtml("<img src=\"missing.png\">"), existing, overwrite: true));
        Assert.Equal("PREVIOUS", await File.ReadAllTextAsync(existing));

        // Failure exactly at the commit point.
        converter.BeforeFileCommit = (_, _) => throw new IOException("simulated disk failure");
        Assert.Equal(PdfConversionError.OutputFailed, (await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertToFileAsync(Quick, existing, overwrite: true))).Error);
        Assert.Equal("PREVIOUS", await File.ReadAllTextAsync(existing));

        // Cancellation immediately before commit.
        using (var cts = new CancellationTokenSource())
        {
            converter.BeforeFileCommit = (_, _) =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertToFileAsync(Quick, existing, overwrite: true, cancellationToken: cts.Token));
            Assert.Equal("PREVIOUS", await File.ReadAllTextAsync(existing));
        }

        // Cancellation during rendering of a file conversion.
        converter.BeforeFileCommit = null;
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertToFileAsync(Slow, _output.Combine("never.pdf"), cancellationToken: cts.Token));
        }

        Assert.False(File.Exists(_output.Combine("never.pdf")));
        Assert.Empty(Directory.EnumerateFiles(_output.Path, "*.tmp"));

        // Explicit overwrite replaces the file with a complete PDF.
        var replaced = await converter.ConvertToFileAsync(Quick, existing, overwrite: true);
        using (var pdf = new PdfInspector(await File.ReadAllBytesAsync(replaced.FilePath)))
        {
            Assert.Contains("quick", pdf.AllText);
        }

        Assert.Equal(PdfConversionError.OutputFailed, (await Assert.ThrowsAsync<PdfConversionException>(() =>
            converter.ConvertToFileAsync(Quick, Path.Combine(_output.Path, "no-such-dir", "a.pdf")))).Error);
        Assert.Empty(Directory.EnumerateFiles(_output.Path, "*.tmp"));
    }

    [Fact]
    public async Task Stream_output_failures_are_reported_as_output_errors()
    {
        await using var converter = Create();
        await using var broken = new FailingStream();
        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertToStreamAsync(Quick, broken));
        Assert.Equal(PdfConversionError.OutputFailed, ex.Error);
    }

    [Fact]
    public async Task A_browser_crash_during_rendering_is_retried_and_the_converter_recovers()
    {
        await using var converter = Create(o => o.MaxAttempts = 2);
        await converter.WarmUpAsync();
        var pid = converter.Host.CurrentProcessId!.Value;

        var conversion = converter.ConvertAsync(Brief("crash"));
        await _server.WaitForRequestAsync("/brief.svg?crash", TimeSpan.FromSeconds(20)); // mid-render
        Assert.Equal(pid, converter.Host.CurrentProcessId);
        Process.GetProcessById(pid).Kill(entireProcessTree: true);

        var result = await conversion;
        Assert.Contains(result.Warnings, w => w.Code == ConversionWarningCode.Retried);
        using (var pdf = new PdfInspector(result.Content))
        {
            Assert.Contains("crash", pdf.AllText);
        }

        Assert.NotEqual(pid, converter.Host.CurrentProcessId);
        Assert.NotNull(converter.GetHealth().LastBrowserFailure);
        await AssertRecoveredAsync(converter);
    }

    [Fact]
    public async Task Without_retries_a_browser_crash_fails_only_the_affected_conversions()
    {
        await using var converter = Create(o =>
        {
            o.MaxAttempts = 1;
            o.Capacity.MaxConcurrentConversions = 2;
        });
        await converter.WarmUpAsync();
        var pid = converter.Host.CurrentProcessId!.Value;
        var inFlight = converter.ConvertAsync(Brief("a"));
        await _server.WaitForRequestAsync("/brief.svg?a", TimeSpan.FromSeconds(20)); // mid-render
        Process.GetProcessById(pid).Kill(entireProcessTree: true);

        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => inFlight);
        Assert.Equal(PdfConversionError.RenderingFailed, ex.Error);

        // Work submitted after the crash runs on a new browser.
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => converter.ConvertAsync(PdfSource.FromHtml($"<p>after-{i}</p>"))));
        Assert.All(results, r => Assert.True(r.ByteCount > 0));
        await AssertRecoveredAsync(converter);
    }

    [Fact]
    public async Task Missing_browser_fails_predictably_without_downloads_or_leftovers()
    {
        var options = TestEnvironment.Options(o =>
        {
            o.Browser.ExecutablePath = Path.Combine(_output.Path, "no-chrome-here");
            o.Browser.SearchStandardLocations = false;
        });
        await using var converter = new Html2PdfConverter(options);
        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertAsync(Quick));
        Assert.Equal(PdfConversionError.RendererUnavailable, ex.Error);
        Assert.DoesNotContain(_output.Path, ex.Message);
        Assert.Equal(PdfConversionError.RendererUnavailable, (await Assert.ThrowsAsync<PdfConversionException>(() => converter.WarmUpAsync())).Error);
        await converter.DisposeAsync();
        Assert.Empty(Directory.EnumerateFileSystemEntries(options.Browser.TemporaryDirectory!, "*", SearchOption.AllDirectories)
            .Where(p => !Directory.Exists(p) || Directory.EnumerateFileSystemEntries(p).Any()));
    }

    [Fact]
    public async Task Launch_timeouts_fail_and_leave_no_processes_or_profiles()
    {
        var options = TestEnvironment.Options(o => o.Browser.LaunchTimeout = TimeSpan.FromMilliseconds(1));
        await using (var converter = new Html2PdfConverter(options))
        {
            var ex = await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertAsync(Quick));
            Assert.Equal(PdfConversionError.RendererUnavailable, ex.Error);
            Assert.Equal(Html2PdfStatus.Unavailable, converter.GetHealth().Status);
        }

        await AssertNoLeftoversAsync(options);
    }

    [Fact]
    public async Task Shutdown_during_startup_releases_everything()
    {
        for (var i = 0; i < 3; i++)
        {
            var options = TestEnvironment.Options();
            var converter = new Html2PdfConverter(options);
            var conversion = converter.ConvertAsync(Quick);
            await Task.Delay(i * 150);
            await converter.ShutdownAsync();
            try
            {
                await conversion;
            }
            catch (PdfConversionException ex)
            {
                Assert.Equal(PdfConversionError.ShuttingDown, ex.Error);
            }

            Assert.Equal(Html2PdfStatus.Stopped, converter.GetHealth().Status);
            await AssertNoLeftoversAsync(options);
        }
    }

    [Fact]
    public async Task Shutdown_settles_active_and_queued_work_within_its_timeout_and_rejects_new_work()
    {
        var options = TestEnvironment.Options(o =>
        {
            o.Network.AllowedHosts.Add("127.0.0.1");
            o.Network.AllowHttp = true;
            o.Network.AllowedPorts.Add(_server.Port);
            o.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
            o.Capacity.MaxConcurrentConversions = 1;
            o.ShutdownTimeout = TimeSpan.FromSeconds(1);
        });
        var converter = new Html2PdfConverter(options);
        await converter.WarmUpAsync();
        var active = converter.ConvertAsync(Slow);
        await Task.Delay(300);
        var queued = converter.ConvertAsync(Quick);
        await Task.Delay(100);

        var watch = Stopwatch.StartNew();
        await converter.ShutdownAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"shutdown took {watch.Elapsed}");

        Assert.Equal(PdfConversionError.ShuttingDown, (await Assert.ThrowsAsync<PdfConversionException>(() => queued)).Error);
        Assert.Equal(PdfConversionError.ShuttingDown, (await Assert.ThrowsAsync<PdfConversionException>(() => active)).Error);
        Assert.Equal(PdfConversionError.ShuttingDown, (await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertAsync(Quick))).Error);
        await converter.ShutdownAsync(); // idempotent
        converter.Dispose();
        await AssertNoLeftoversAsync(options);
    }

    [Fact]
    public async Task Graceful_shutdown_lets_accepted_work_finish()
    {
        var options = TestEnvironment.Options(o =>
        {
            o.Network.AllowedHosts.Add("127.0.0.1");
            o.Network.AllowHttp = true;
            o.Network.AllowedPorts.Add(_server.Port);
            o.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
            o.ShutdownTimeout = TimeSpan.FromSeconds(20);
        });
        var converter = new Html2PdfConverter(options);
        await converter.WarmUpAsync();
        var conversions = Enumerable.Range(0, 3).Select(i => converter.ConvertAsync(Brief($"graceful{i}"))).ToArray();
        await Task.Delay(300);
        await Task.WhenAll(converter.ShutdownAsync(), converter.DisposeAsync().AsTask());
        foreach (var conversion in conversions)
        {
            Assert.True((await conversion).ByteCount > 0);
        }

        await AssertNoLeftoversAsync(options);
    }

    [Fact]
    public async Task Repeated_creation_and_disposal_does_not_leak()
    {
        var options = TestEnvironment.Options();
        for (var i = 0; i < 6; i++)
        {
            await using var converter = new Html2PdfConverter(options);
            await converter.ConvertAsync(Quick);
        }

        await AssertNoLeftoversAsync(options);
    }

    [Fact]
    public async Task Broken_logging_and_metrics_listeners_do_not_affect_conversions()
    {
        using var factory = LoggerFactory.Create(b => b.AddProvider(new CapturingLoggerProvider(throwOnLog: true)).SetMinimumLevel(LogLevel.Trace));
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Nast.Html2Pdf")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("listener failure"));
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("listener failure"));
        listener.Start();

        await using var converter = new Html2PdfConverter(TestEnvironment.Options(), factory);
        var result = await converter.ConvertAsync(Quick);
        Assert.True(result.ByteCount > 0);
        listener.RecordObservableInstruments();
        Assert.Equal(PdfConversionError.TemplateError, (await Assert.ThrowsAsync<PdfConversionException>(() =>
            converter.ConvertAsync(PdfSource.FromTemplate("{{ Missing }}", null)))).Error);
    }

    [Fact]
    public async Task Metrics_report_outcomes_and_saturation()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentBag<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Nast.Html2Pdf")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((i, v, t, _) => measurements.Add((i.Name, v, t.ToArray())));
        listener.SetMeasurementEventCallback<int>((i, v, t, _) => measurements.Add((i.Name, v, t.ToArray())));
        listener.SetMeasurementEventCallback<double>((i, v, t, _) => measurements.Add((i.Name, v, t.ToArray())));
        listener.Start();

        await using var converter = Create();
        var marker = Guid.NewGuid().ToString("N");
        await converter.ConvertAsync(PdfSource.FromHtml($"<p>{marker}</p>"));
        await Assert.ThrowsAsync<PdfConversionException>(() => converter.ConvertAsync(PdfSource.FromTemplate("{{ x", null)));
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, m => m.Name == "html2pdf.conversions" && m.Tags.Any(t => t.Value as string == "success"));
        Assert.Contains(measurements, m => m.Name == "html2pdf.conversions" && m.Tags.Any(t => t.Value as string == "TemplateError"));
        Assert.Contains(measurements, m => m.Name == "html2pdf.conversion.duration");
        Assert.Contains(measurements, m => m.Name == "html2pdf.browser.launches");
        Assert.Contains(measurements, m => m.Name == "html2pdf.conversions.active");
        var health = converter.GetHealth();
        Assert.Equal(Html2PdfStatus.Ready, health.Status);
        Assert.True(health.IsAcceptingWork);
        Assert.Equal(1, health.SucceededConversions);
        Assert.Equal(1, health.FailedConversions);
        Assert.StartsWith("Chrome", health.BrowserVersion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task AssertNoLeftoversAsync(Html2PdfOptions options)
    {
        var profiles = options.Browser.TemporaryDirectory!;
        Assert.Equal(0, await ProcessProbe.WaitForNoProcessesAsync(profiles, TimeSpan.FromSeconds(10)));
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (Directory.EnumerateDirectories(profiles, "*", SearchOption.AllDirectories).Any(d => Path.GetFileName(Path.GetDirectoryName(d)) == "nast-html2pdf") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Empty(Directory.EnumerateDirectories(profiles, "*", SearchOption.AllDirectories).Where(d => Path.GetFileName(Path.GetDirectoryName(d)) == "nast-html2pdf"));
    }

    public void Dispose()
    {
        _server.Dispose();
        _output.Dispose();
    }

    private sealed class FailingStream : MemoryStream
    {
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new IOException("disk full");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("disk full");
    }
}
