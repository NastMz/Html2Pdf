// Measures the practical operating envelope and enforces release thresholds.
// Usage: dotnet run -c Release -- [--thresholds eng/perf-thresholds.json] [--report artifacts/perf.json] [--quick]
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Nast.Html2Pdf;

var arguments = args.ToList();
string? Argument(string name) => arguments.IndexOf(name) is var i and >= 0 && i + 1 < arguments.Count ? arguments[i + 1] : null;
var quick = arguments.Contains("--quick");
var thresholdsPath = Argument("--thresholds");
var reportPath = Argument("--report");
var disableSandbox = Environment.GetEnvironmentVariable("HTML2PDF_TEST_DISABLE_SANDBOX") == "true" || (OperatingSystem.IsLinux() && Environment.UserName == "root");
var concurrency = int.Parse(Argument("--concurrency") ?? "4", CultureInfo.InvariantCulture);

const string Invoice = """
    <!DOCTYPE html><html><head><meta charset="utf-8"><style>
      body { font-family: sans-serif; font-size: 11pt } table { width: 100%; border-collapse: collapse }
      td, th { border-bottom: 1px solid #ccc; padding: 2mm } .total { font-weight: bold }
    </style></head><body>
    <h1>Invoice {{ Number }}</h1><p>{{ Customer }}</p>
    <table><tr><th>Item</th><th>Qty</th><th>Total</th></tr>
    {% for l in Lines %}<tr><td>{{ l.Item }}</td><td>{{ l.Qty }}</td><td>{{ l.Total | format_number: 'N2' }}</td></tr>{% endfor %}
    </table><p class="total">Total {{ Total | format_number: 'N2' }}</p></body></html>
    """;
object InvoiceModel(int i) => new
{
    Number = $"INV-{i:D5}",
    Customer = "Contoso Ltd",
    Lines = Enumerable.Range(1, 15).Select(n => new { Item = $"Line item {n}", Qty = n, Total = n * 12.5m }).ToArray(),
    Total = 1500m,
};
var report = "<!DOCTYPE html><html><body style=\"font-family:sans-serif\"><h1>Report</h1><table>" +
             string.Concat(Enumerable.Range(1, 400).Select(i => $"<tr><td>Row {i}</td><td>{i * 7}</td><td>Region {i % 9}</td></tr>")) + "</table></body></html>";

Html2PdfOptions Options(int maxConcurrency) => new()
{
    Browser = { DisableSandbox = disableSandbox, TemporaryDirectory = Directory.CreateTempSubdirectory("html2pdf-bench-").FullName },
    Capacity = { MaxConcurrentConversions = maxConcurrency, MaxQueueLength = 256, QueueTimeout = TimeSpan.FromMinutes(2) },
};

static double Percentile(List<double> values, double p)
{
    var sorted = values.OrderBy(v => v).ToList();
    var index = (int)Math.Ceiling(p / 100 * sorted.Count) - 1;
    return Math.Round(sorted[Math.Clamp(index, 0, sorted.Count - 1)], 1);
}

var results = new Dictionary<string, double>();

// 1. Cold start: a new converter, first conversion includes launching Chromium.
var cold = new List<double>();
for (var i = 0; i < (quick ? 2 : 3); i++)
{
    await using var converter = new Html2PdfConverter(Options(1));
    var watch = Stopwatch.StartNew();
    await converter.ConvertAsync(PdfSource.FromTemplate(Invoice, InvoiceModel(i)));
    cold.Add(watch.Elapsed.TotalMilliseconds);
}

results["cold_start_ms_median"] = Percentile(cold, 50);

// 2. Warm latency, sequential.
await using (var converter = new Html2PdfConverter(Options(1)))
{
    await converter.WarmUpAsync();
    for (var i = 0; i < 3; i++)
    {
        await converter.ConvertAsync(PdfSource.FromTemplate(Invoice, InvoiceModel(i)));
    }

    var warm = new List<double>();
    for (var i = 0; i < (quick ? 15 : 40); i++)
    {
        var watch = Stopwatch.StartNew();
        await converter.ConvertAsync(PdfSource.FromTemplate(Invoice, InvoiceModel(i)));
        warm.Add(watch.Elapsed.TotalMilliseconds);
    }

    results["warm_invoice_ms_p50"] = Percentile(warm, 50);
    results["warm_invoice_ms_p95"] = Percentile(warm, 95);

    var multipage = new List<double>();
    var pages = 0L;
    for (var i = 0; i < (quick ? 4 : 10); i++)
    {
        var watch = Stopwatch.StartNew();
        var document = await converter.ConvertAsync(PdfSource.FromHtml(report));
        multipage.Add(watch.Elapsed.TotalMilliseconds);
        pages = document.ByteCount;
    }

    results["warm_report_ms_p50"] = Percentile(multipage, 50);
    results["report_pdf_kb"] = Math.Round(pages / 1024.0, 1);
}

// 3. Throughput at full concurrency with peak resident memory of the whole process tree.
await using (var converter = new Html2PdfConverter(Options(concurrency)))
{
    await converter.WarmUpAsync();
    var total = quick ? 60 : 200;
    var peak = 0L;
    using var sampling = new CancellationTokenSource();
    var sampler = Task.Run(async () =>
    {
        while (!sampling.IsCancellationRequested)
        {
            peak = Math.Max(peak, ProcessTreeResidentBytes());
            await Task.Delay(100);
        }
    });

    var latencies = new List<double>();
    var watch = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, total).Select(async i =>
    {
        var result = await converter.ConvertAsync(i % 5 == 0 ? PdfSource.FromHtml(report) : PdfSource.FromTemplate(Invoice, InvoiceModel(i)));
        lock (latencies)
        {
            // Service time under load: total minus time spent waiting in the queue.
            latencies.Add((result.Timings.Total - result.Timings.Queued).TotalMilliseconds);
        }
    }));
    var elapsed = watch.Elapsed;
    await sampling.CancelAsync();
    await sampler;

    results["throughput_docs_per_s"] = Math.Round(total / elapsed.TotalSeconds, 2);
    results["loaded_service_ms_p95"] = Percentile(latencies, 95);
    results["peak_rss_mb"] = Math.Round(peak / 1048576.0, 0);
    results["concurrency"] = concurrency;
}

Console.WriteLine($"Environment: {Environment.ProcessorCount} logical CPUs, {Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0, 1)} GB, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}");
Console.WriteLine("| Metric | Value |");
Console.WriteLine("|---|---|");
foreach (var (name, value) in results)
{
    Console.WriteLine($"| {name} | {value.ToString(CultureInfo.InvariantCulture)} |");
}

var violations = new List<string>();
if (thresholdsPath is not null)
{
    using var thresholds = JsonDocument.Parse(File.ReadAllText(thresholdsPath));
    foreach (var rule in thresholds.RootElement.GetProperty("thresholds").EnumerateObject())
    {
        var value = results[rule.Name];
        if (rule.Value.TryGetProperty("max", out var max) && value > max.GetDouble())
        {
            violations.Add($"{rule.Name} = {value} exceeds {max.GetDouble()}");
        }

        if (rule.Value.TryGetProperty("min", out var min) && value < min.GetDouble())
        {
            violations.Add($"{rule.Name} = {value} is below {min.GetDouble()}");
        }
    }
}

if (reportPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    File.WriteAllText(reportPath, JsonSerializer.Serialize(new { results, violations, cpus = Environment.ProcessorCount, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription }, new JsonSerializerOptions { WriteIndented = true }));
}

foreach (var violation in violations)
{
    Console.Error.WriteLine("THRESHOLD VIOLATION: " + violation);
}

return violations.Count == 0 ? 0 : 1;

// Resident memory of this process and all of its descendants (Linux /proc); the managed process otherwise.
static long ResidentBytesOf(int pid)
{
    try
    {
        var line = File.ReadLines($"/proc/{pid}/status").FirstOrDefault(l => l.StartsWith("VmRSS:", StringComparison.Ordinal));
        return line is null ? 0 : long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) * 1024;
    }
    catch (IOException)
    {
        return 0;
    }
}

static long ProcessTreeResidentBytes()
{
    if (!OperatingSystem.IsLinux())
    {
        return Process.GetCurrentProcess().WorkingSet64;
    }

    var parents = new Dictionary<int, int>();
    foreach (var directory in Directory.EnumerateDirectories("/proc"))
    {
        if (int.TryParse(Path.GetFileName(directory), out var pid))
        {
            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                parents[pid] = int.Parse(fields[1], CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
            {
            }
        }
    }

    var self = Environment.ProcessId;
    long total = 0;
    foreach (var pid in parents.Keys)
    {
        for (var current = pid; current > 1 && parents.TryGetValue(current, out var parent); current = parent)
        {
            if (current == self)
            {
                total += ResidentBytesOf(pid);
                break;
            }
        }
    }

    return total;
}
