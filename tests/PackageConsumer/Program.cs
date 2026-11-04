// Package consumer smoke test. Every supported input type, output type, failure path and lifecycle mode is exercised
// through the public API of the installed package. Exit code 0 means every check passed.
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf;

var failures = new List<string>();
var work = Directory.CreateTempSubdirectory("html2pdf-consumer-").FullName;
var disableSandbox = Environment.GetEnvironmentVariable("HTML2PDF_TEST_DISABLE_SANDBOX") == "true" || (OperatingSystem.IsLinux() && Environment.UserName == "root");

void Check(string name, bool condition, string detail = "")
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? " — " + detail : string.Empty)}");
    if (!condition)
    {
        failures.Add(name);
    }
}

static bool LooksLikePdf(ReadOnlyMemory<byte> pdf)
{
    var text = Encoding.ASCII.GetString(pdf.Span);
    return text.StartsWith("%PDF-", StringComparison.Ordinal) && text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal) && text.Contains("/Type /Page", StringComparison.Ordinal);
}

async Task Expect(string name, PdfConversionError expected, Func<Task> action)
{
    try
    {
        await action();
        Check(name, false, "no exception");
    }
    catch (PdfConversionException ex)
    {
        Check(name, ex.Error == expected && ex.OperationId is not null, $"{ex.Error}: {ex.Message}");
    }
}

// Local content and a loopback "remote" server for URL sources.
File.WriteAllText(Path.Combine(work, "style.css"), "h1 { color: #003366; }");
File.WriteAllText(Path.Combine(work, "report.html"), "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head><body><h1>Local report</h1></body></html>");
File.WriteAllText(Path.Combine(work, "letter.liquid"), "<h1>Dear {{ Name }}</h1>{% render 'signature' %}");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
using var http = new HttpListener();
http.Prefixes.Add($"http://127.0.0.1:{port}/");
http.Start();
_ = Task.Run(async () =>
{
    while (http.IsListening)
    {
        try
        {
            var context = await http.GetContextAsync();
            var body = Encoding.UTF8.GetBytes("<!DOCTYPE html><h1>Remote page</h1>");
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            return;
        }
    }
});

void Configure(Html2PdfOptions options)
{
    options.Browser.DisableSandbox = disableSandbox;
    options.LocalFiles.AllowedRoots.Add(work);
    options.Network.AllowUrlSources = true;
    options.Network.AllowHttp = true;
    options.Network.AllowedHosts.Add("127.0.0.1");
    options.Network.AllowedPorts.Add(port);
    options.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
}

// 1. Dependency injection.
var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("HTML2PDF_LOG_LEVEL"), out var level) ? level : LogLevel.Warning));
services.AddHtml2Pdf(Configure);
await using (var provider = services.BuildServiceProvider())
{
    var converter = provider.GetRequiredService<IHtml2PdfConverter>();
    var model = new { Number = "INV-1", Customer = "<Ada & Co>", Lines = new[] { new { Item = "Work", Amount = 10.5m } } };
    var invoice = await converter.ConvertAsync(PdfSource.FromTemplate(
        "<h1>Invoice {{ Number }}</h1><p>{{ Customer }}</p>{% for l in Lines %}<p>{{ l.Item }} {{ l.Amount }}</p>{% endfor %}", model));
    Check("template source via DI", LooksLikePdf(invoice.Content), $"{invoice.ByteCount} bytes in {invoice.Timings.Total.TotalMilliseconds:0} ms");

    var html = await converter.ConvertAsync(PdfSource.FromHtml("<h1>Hello 世界 ✓</h1>"), new PdfConversionOptions { PaperSize = PaperSize.Letter, Orientation = PageOrientation.Landscape });
    Check("html source", LooksLikePdf(html.Content));

    var file = await converter.ConvertAsync(PdfSource.FromFile(Path.Combine(work, "report.html")));
    Check("local file source", LooksLikePdf(file.Content) && file.Warnings.Count == 0);

    var templateFile = await converter.ConvertAsync(PdfSource.FromTemplateFile(Path.Combine(work, "letter.liquid"), new { Name = "Grace" },
        new TemplateSourceOptions { Partials = new Dictionary<string, string> { ["signature"] = "<p>Regards</p>" } }));
    Check("template file source", LooksLikePdf(templateFile.Content));

    var url = await converter.ConvertAsync(PdfSource.FromUrl($"http://127.0.0.1:{port}/page"));
    Check("url source", LooksLikePdf(url.Content));

    var output = Path.Combine(work, "out.pdf");
    var written = await converter.ConvertToFileAsync(PdfSource.FromHtml("<p>file</p>"), output);
    Check("file output", File.Exists(written.FilePath) && LooksLikePdf(await File.ReadAllBytesAsync(output)));
    await Expect("existing file protected", PdfConversionError.OutputFailed, () => converter.ConvertToFileAsync(PdfSource.FromHtml("<p>x</p>"), output));
    await converter.ConvertToFileAsync(PdfSource.FromHtml("<p>replaced</p>"), output, overwrite: true);

    using var stream = new MemoryStream();
    await converter.ConvertToStreamAsync(PdfSource.FromHtml("<p>stream</p>"), stream);
    Check("stream output", LooksLikePdf(stream.ToArray()));

    await Expect("remote resource denied by default host list", PdfConversionError.AccessDenied, () => converter.ConvertAsync(PdfSource.FromHtml("<img src=\"https://example.com/a.png\">")));
    await Expect("path outside roots denied", PdfConversionError.AccessDenied, () => converter.ConvertAsync(PdfSource.FromFile(Path.Combine(Path.GetTempPath(), "outside.html"))));
    await Expect("template error", PdfConversionError.TemplateError, () => converter.ConvertAsync(PdfSource.FromTemplate("{{ Missing }}", null)));
    await Expect("invalid layout", PdfConversionError.InvalidRequest, () => converter.ConvertAsync(PdfSource.FromHtml("x"), new PdfConversionOptions { Header = new PdfHeaderFooter("h", "50mm") }));
    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        try
        {
            await converter.ConvertAsync(PdfSource.FromHtml("x"), cancellationToken: cts.Token);
            Check("cancellation", false);
        }
        catch (OperationCanceledException)
        {
            Check("cancellation", true);
        }
    }

    var health = converter.GetHealth();
    Check("health", health.Status == Html2PdfStatus.Ready && health.BrowserVersion is not null, $"{health.Status}, {health.BrowserVersion}");
}

// 2. Explicit creation and disposal, with secure defaults (no network, no files, no scripts).
var defaults = new Html2PdfOptions();
defaults.Browser.DisableSandbox = disableSandbox;
await using (var standalone = new Html2PdfConverter(defaults))
{
    await standalone.WarmUpAsync();
    var result = await standalone.ConvertAsync(PdfSource.FromHtml("<p id=x>static</p><script>document.getElementById('x').textContent='ran'</script>"));
    Check("explicit lifecycle", LooksLikePdf(result.Content));
    await Expect("url sources disabled by default", PdfConversionError.AccessDenied, () => standalone.ConvertAsync(PdfSource.FromUrl($"http://127.0.0.1:{port}/")));
    await standalone.ShutdownAsync();
    Check("stopped after shutdown", standalone.GetHealth().Status == Html2PdfStatus.Stopped);
    await Expect("rejected after shutdown", PdfConversionError.ShuttingDown, () => standalone.ConvertAsync(PdfSource.FromHtml("x")));
}

http.Stop();
Directory.Delete(work, recursive: true);
Console.WriteLine(failures.Count == 0 ? $"ALL CHECKS PASSED ({Environment.Version})" : $"{failures.Count} CHECK(S) FAILED: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;
