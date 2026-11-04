using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Nast.Html2Pdf.Diagnostics;

/// <summary>
/// Metrics and tracing. Instrument names are part of the public contract and documented in docs/operations.md.
/// Every emission is guarded so that a failing listener cannot affect conversions.
/// </summary>
internal sealed class Html2PdfMetrics : IDisposable
{
    public const string MeterName = "Nast.Html2Pdf";
    internal static readonly ActivitySource ActivitySource = new(MeterName, Version);

    private readonly Meter _meter;
    private readonly Counter<long> _conversions;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _queueWait;
    private readonly Counter<long> _rejections;
    private readonly Counter<long> _browserLaunches;
    private readonly Counter<long> _retries;

    public Html2PdfMetrics(Func<int> active, Func<int> queued, Func<int> browserRunning)
    {
        _meter = new Meter(MeterName, Version);
        _conversions = _meter.CreateCounter<long>("html2pdf.conversions", description: "Completed conversions by outcome.");
        _duration = _meter.CreateHistogram<double>("html2pdf.conversion.duration", unit: "s", description: "End-to-end conversion duration.");
        _queueWait = _meter.CreateHistogram<double>("html2pdf.queue.duration", unit: "s", description: "Time spent waiting for a rendering slot.");
        _rejections = _meter.CreateCounter<long>("html2pdf.capacity.rejections", description: "Conversions rejected for lack of capacity.");
        _browserLaunches = _meter.CreateCounter<long>("html2pdf.browser.launches", description: "Browser launch attempts by outcome.");
        _retries = _meter.CreateCounter<long>("html2pdf.retries", description: "Conversion attempts retried after a renderer infrastructure failure.");
        _meter.CreateObservableUpDownCounter("html2pdf.conversions.active", () => Guard(active), description: "Conversions holding a rendering slot.");
        _meter.CreateObservableUpDownCounter("html2pdf.conversions.queued", () => Guard(queued), description: "Conversions waiting for a rendering slot.");
        _meter.CreateObservableUpDownCounter("html2pdf.browser.running", () => Guard(browserRunning), description: "1 when a browser process is running.");
    }

    private static string Version => typeof(Html2PdfMetrics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public void ConversionCompleted(string source, string outcome, string? error, TimeSpan duration)
    {
        Safe(() =>
        {
            var tags = new TagList { { "source", source }, { "outcome", outcome } };
            if (error is not null)
            {
                tags.Add("error", error);
            }

            _conversions.Add(1, tags);
            _duration.Record(duration.TotalSeconds, tags);
        });
    }

    public void QueueWaited(TimeSpan wait) => Safe(() => _queueWait.Record(wait.TotalSeconds));

    public void Rejected(string reason) => Safe(() => _rejections.Add(1, new KeyValuePair<string, object?>("reason", reason)));

    public void BrowserLaunched(bool success, TimeSpan duration)
        => Safe(() => _browserLaunches.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failure")));

    public void Retried() => Safe(() => _retries.Add(1));

    public static Activity? StartActivity(string operationId, string source)
    {
        try
        {
            var activity = ActivitySource.StartActivity("html2pdf.convert", ActivityKind.Internal);
            activity?.SetTag("html2pdf.operation_id", operationId);
            activity?.SetTag("html2pdf.source", source);
            return activity;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Observability must never change the outcome of a conversion.
        }
    }

    private static int Guard(Func<int> value)
    {
        try
        {
            return value();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0;
        }
    }

    public void Dispose() => _meter.Dispose();
}
