# Nast.Html2Pdf

Convert HTML, Liquid templates, local documents and explicitly authorized URLs to PDF from .NET, using headless Chromium.
Conversion is treated as a potentially hostile operation: network access and document scripts are **off by default**, every
request a document makes is mediated by an explicit policy, conversions are isolated from each other, and capacity,
deadlines, cancellation, retries and file output are bounded and predictable.

- **Inputs:** HTML text, trusted Liquid templates with untrusted data, template files, local HTML files, HTTP(S) URLs.
- **Output:** searchable text, Unicode, embedded fonts, images, links, tables, print CSS, page order, optional bookmarks.
- **Layout:** A4 portrait by default; standard and custom paper sizes, orientation, margins, scale, backgrounds, page
  ranges; headers and footers with page numbers and totals in reserved space.
- **Operations:** dependency injection or explicit lifetime, stable error categories, timings and warnings, health
  snapshots, metrics and traces, graceful shutdown.

Version 2 is a redesign. Coming from 1.x? Read the [migration guide](https://github.com/NastMz/Html2Pdf/blob/main/docs/migration-v2.md).

## Requirements

| | Supported and verified |
|---|---|
| .NET | Applications targeting .NET 8 or later (package targets `net8.0`; tested on .NET 8 and .NET 10) |
| Operating systems | Linux x64, Windows x64, macOS arm64 (full test suite and package verification on each in CI; see [verification](https://github.com/NastMz/Html2Pdf/blob/main/docs/verification.md)) |
| Browser | Google Chrome, Chromium or chrome-headless-shell **120 or later**, installed separately |

**The library never downloads a browser.** Provide one through `Html2PdfOptions.Browser.ExecutablePath`, the
`NAST_HTML2PDF_CHROMIUM_PATH` environment variable, or a standard installation location. A missing or unsupported
browser fails with `PdfConversionError.RendererUnavailable`; call `WarmUpAsync()` at startup to detect it early.
Once Chromium is installed, local documents convert without any Internet access. See
[operations](https://github.com/NastMz/Html2Pdf/blob/main/docs/operations.md) for container images and provisioning.

## Install

```bash
dotnet add package Nast.Html2Pdf
```

## Quick start

```csharp
using Nast.Html2Pdf;

await using var converter = new Html2PdfConverter(new Html2PdfOptions());

PdfDocument pdf = await converter.ConvertAsync(PdfSource.FromHtml("<h1>Hello, PDF</h1>"));
await File.WriteAllBytesAsync("hello.pdf", pdf.ToArray());
```

With dependency injection the container owns the converter and shuts the browser down on disposal:

```csharp
builder.Services.AddHtml2Pdf(options =>
{
    options.LocalFiles.AllowedRoots.Add(Path.Combine(builder.Environment.ContentRootPath, "templates"));
});

// Inject IHtml2PdfConverter where needed.
```

Options are bindable, so `builder.Services.Configure<Html2PdfOptions>(builder.Configuration.GetSection("Html2Pdf"))`
before `AddHtml2Pdf()` also works.

## Inputs

```csharp
// Markup you produced. Relative resources resolve against an optional authorized directory.
PdfSource.FromHtml(html, baseDirectory: "/srv/app/assets");

// A trusted Liquid template and untrusted data: model values are HTML-encoded and never executed.
PdfSource.FromTemplate("<h1>Invoice {{ Number }}</h1><p>{{ Customer.Name }}</p>", invoice);

// A template file and trusted partials ({% render 'name' %}); resources resolve against the file's directory.
PdfSource.FromTemplateFile("/srv/app/templates/invoice.liquid", invoice,
    new TemplateSourceOptions { Partials = new Dictionary<string, string> { ["address"] = "<p>{{ Street }}</p>" } });

// A local HTML file under LocalFiles.AllowedRoots, with its CSS, images and fonts.
PdfSource.FromFile("/srv/app/reports/q3/index.html");

// A remote page: requires Network.AllowUrlSources and the host in Network.AllowedHosts.
PdfSource.FromUrl("https://reports.example.com/q3");
```

Template syntax, escaping rules, `TrustedHtml`, partials and headers are described in
[templates](https://github.com/NastMz/Html2Pdf/blob/main/docs/templates.md).

## Layout, headers and footers

```csharp
var options = new PdfConversionOptions
{
    PaperSize = PaperSize.Letter,                  // or PaperSize.Custom("100mm", "150mm")
    Orientation = PageOrientation.Landscape,
    Margins = new PageMargins(top: "25mm", right: "15mm", bottom: "20mm", left: "15mm"),
    Header = new PdfHeaderFooter("<div style=\"font-size:9pt\">{{ Company }}</div>", height: "10mm"),
    Footer = new PdfHeaderFooter("<div style=\"text-align:right\">Page {{ pdf.page_number }} of {{ pdf.total_pages }}</div>", "8mm"),
    HeaderFooterModel = new { Company = "Contoso" },
};
```

- The job's paper size, orientation and margins are authoritative (`LayoutPrecedence.Job`, the default) and override a
  document's CSS `@page` size and margins. Use `LayoutPrecedence.Document` to let the document decide instead.
- Header and footer bands sit directly above and below the body inside the margin. A band taller than its margin, content
  taller than its band, external resources in a band, or scripts in a band fail the conversion rather than overlapping,
  clipping or printing placeholders.
- Contradictory settings (margins larger than the page, scale outside 0.1–2, invalid page ranges) fail with
  `PdfConversionError.InvalidRequest`.

## Results and errors

Every call returns a successful result or throws. There is no partially successful state to check.

```csharp
try
{
    PdfFileResult result = await converter.ConvertToFileAsync(source, "/data/out/invoice.pdf", overwrite: false, options, cancellationToken);
    logger.LogInformation("Wrote {Bytes} bytes in {Ms} ms (operation {Id}, {Warnings} warnings)",
        result.ByteCount, result.Timings.Total.TotalMilliseconds, result.OperationId, result.Warnings.Count);
}
catch (PdfConversionException ex) when (ex.Error == PdfConversionError.CapacityExceeded)
{
    // Back off and retry later (ex.IsTransient is true).
}
catch (PdfConversionException ex)
{
    // ex.Error: InvalidRequest, AccessDenied, ResourceUnavailable, TemplateError, CapacityExceeded, Timeout,
    // ReadinessNotMet, RenderingFailed, OutputFailed, RendererUnavailable or ShuttingDown.
    // ex.Message is safe to log: no document content, model data, query strings or absolute host paths.
}
// Cancellation through your token surfaces as OperationCanceledException.
```

- `ConvertAsync` returns the PDF in memory, `ConvertToStreamAsync` writes it to a stream, and `ConvertToFileAsync`
  commits it atomically: a temporary file is written, flushed and moved into place. An existing file is replaced only
  with `overwrite: true`, and is left intact when anything fails. Cancellation is observed until the commit point.
- Required sub-resources (images, styles, fonts, frames) that are missing or denied fail the conversion. Set
  `ResourceFailures = ResourceFailurePolicy.BestEffort` to continue and receive a `ConversionWarning` per resource instead.
- `Timeout` (default 60 s) covers queueing, preparation, rendering, retries and output. `Readiness.WaitForSelector`,
  `WaitForExpression` and `NetworkIdleTime` add explicit readiness conditions, reported as `ReadinessNotMet` if unmet.

## Security defaults

| Capability | Default | Enable with |
|---|---|---|
| Remote resources | Off | `Network.AllowedHosts` (exact hosts or `*.domain`) |
| URL sources | Off | `Network.AllowUrlSources` plus an allowed host |
| Plain HTTP | Off | `Network.AllowHttp` |
| Private/loopback/link-local/metadata addresses | Always refused | `Network.AllowedPrivateNetworks` (CIDR), e.g. for intranet hosts |
| Local files | Off | `LocalFiles.AllowedRoots` |
| Document scripts | Off | `AllowScripts` |
| Chromium sandbox | On | `Browser.DisableSandbox` (only where the environment provides equivalent isolation) |

Chromium has no direct network access: every request is answered by the library, which checks the policy, resolves DNS
itself and connects only to validated addresses (so redirects and DNS rebinding are re-checked on every hop). No cookies,
credentials or proxies of the host application are used, and each conversion runs in a fresh browser context. Details,
guarantees and their limits are in [security](https://github.com/NastMz/Html2Pdf/blob/main/docs/security.md).

## Capacity, health and shutdown

```csharp
options.Capacity.MaxConcurrentConversions = 4;   // rendering slots
options.Capacity.MaxQueueLength = 32;            // waiting requests; more fail fast with CapacityExceeded
options.Capacity.QueueTimeout = TimeSpan.FromSeconds(30);
options.ShutdownTimeout = TimeSpan.FromSeconds(30);

Html2PdfHealth health = converter.GetHealth();   // Status, active/queued counts, browser version, last failure
await converter.ShutdownAsync();                 // stop intake, settle work, release Chromium and temp files
```

Metrics (`Nast.Html2Pdf` meter), traces (`Nast.Html2Pdf` activity source), log events and a health-check example are
documented in [operations](https://github.com/NastMz/Html2Pdf/blob/main/docs/operations.md).

## Scope and limitations

This is a library, not a conversion service. It does not claim PDF/A, PDF/UA, digital signatures or encryption, and it
does not promise pixel-identical output across Chromium versions or operating systems (fonts differ between systems;
embed web fonts for consistent typography). With scripts enabled, WebSockets, workers, service workers and popups are
not supported. The [verification report](https://github.com/NastMz/Html2Pdf/blob/main/docs/verification.md) lists what
has been verified, how, and under which conditions.

## License

MIT
