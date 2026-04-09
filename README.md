# Nast.Html2Pdf

`Nast.Html2Pdf` is a secure-by-default HTML-to-PDF library that targets `net8.0` and is intended for **.NET 8+** consuming applications. It renders RazorLight templates to HTML and uses PuppeteerSharp/Chromium for PDF generation.

## Runtime Support

- Package target: `net8.0`
- Supported consumer runtimes: **.NET 8+**
- Platforms: Windows, Linux, macOS
- Browser engine: Chromium through PuppeteerSharp

## Installation

```bash
dotnet add package Nast.Html2Pdf
```

```xml
<PackageReference Include="Nast.Html2Pdf" Version="1.0.1" />
```

## Secure Defaults

The library starts from a conservative Chromium baseline.

- `AllowInsecureCertificates = false`
- `DisableSandbox = false`
- no permissive `--disable-web-security` default
- invalid template, HTML, file path, URL, and PDF option inputs fail before expensive browser work

Only opt into weaker settings when your environment truly requires them.

## Supported Entry Points

Primary service API via `IHtml2PdfService`:

- `GeneratePdfAsync(template, model, pdfOptions, htmlOptions)` for Razor templates plus model data
- `GeneratePdfFromFileAsync(templatePath, model, pdfOptions, htmlOptions)` for template files
- `GeneratePdfFromHtmlAsync(html, pdfOptions)` for already-rendered HTML
- `GeneratePdfFromUrlAsync(url, pdfOptions)` for reachable `http` or `https` URLs

Additional convenience entry points:

- `Html2PdfFactory.Create(...)` and `Html2PdfFactory.CreateDefault()` for non-DI scenarios
- `Html2PdfServiceExtensions` helpers to write PDFs directly to files or streams

## DI Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 1;
    options.MaxInstances = 3;
    options.AcquireTimeoutSeconds = 30;
});

using var provider = services.BuildServiceProvider();
var html2Pdf = provider.GetRequiredService<IHtml2PdfService>();
```

`AddHtml2Pdf()` registers:

- `IBrowserPool`
- `IHtmlGenerator`
- `IPdfConverter`
- `IHtml2PdfService`
- `Html2PdfDiagnostics`

For advanced registration control, `AddHtml2PdfWithLifetime(...)` lets you override service lifetimes.

## Browser Provisioning

By default, PuppeteerSharp can download Chromium on first use. For locked-down or pre-provisioned environments, configure the browser explicitly.

```csharp
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 1;
    options.MaxInstances = 5;
    options.MaxLifetimeMinutes = 60;
    options.AcquireTimeoutSeconds = 30;
    options.Headless = true;

    options.SkipBrowserDownload = true;
    options.ExecutablePath = @"/usr/bin/chromium";
    options.BrowserCacheDirectory = "/var/cache/nast-html2pdf";
});
```

Provisioning guidance:

- Keep `SkipBrowserDownload = false` if you want automatic Chromium provisioning.
- Set `SkipBrowserDownload = true` only when you manage Chromium yourself.
- Use `ExecutablePath` when your runtime image already contains Chrome/Chromium.
- Use `BrowserCacheDirectory` when you want a predictable PuppeteerSharp cache location.
- `AdditionalArgs` is available for environment-specific Chromium flags.

## Explicit Opt-Ins

These settings are intentionally not enabled unless you request them.

```csharp
services.AddHtml2Pdf(options =>
{
    options.AllowInsecureCertificates = true;
    options.DisableSandbox = true;
    options.AdditionalArgs = ["--window-size=1280,720"];
});
```

Tradeoffs:

- `AllowInsecureCertificates = true` weakens TLS validation.
- `DisableSandbox = true` reduces browser isolation and should be reserved for environments that cannot run Chromium with the sandbox enabled.

## Realistic Examples

### Generate from Raw HTML

```csharp
using Nast.Html2Pdf.Models;

var html = """
<!doctype html>
<html>
<head>
    <meta charset="utf-8" />
    <style>
        body { font-family: Arial, sans-serif; margin: 32px; }
        h1 { margin-bottom: 8px; }
        .muted { color: #666; }
    </style>
</head>
<body>
    <h1>Quarterly Summary</h1>
    <p class="muted">Generated from raw HTML</p>
</body>
</html>
""";

var pdfOptions = new PdfOptions
{
    Format = "A4",
    PrintBackground = true
};

var result = await html2Pdf.GeneratePdfFromHtmlAsync(html, pdfOptions);

if (result.Success && result.Data is not null)
{
    await File.WriteAllBytesAsync("quarterly-summary.pdf", result.Data);
}
```

### Generate from a Razor Template and Model

```csharp
using Nast.Html2Pdf.Helpers;

var template = """
<!doctype html>
<html>
<body>
    <h1>Invoice @Model.Number</h1>
    <p>Customer: @Model.CustomerName</p>
    <table>
        <thead>
            <tr><th>Description</th><th>Total</th></tr>
        </thead>
        <tbody>
        @foreach (var item in Model.Items)
        {
            <tr><td>@item.Description</td><td>@item.Total.ToString("C")</td></tr>
        }
        </tbody>
    </table>
    <p><strong>Grand total: @Model.GrandTotal.ToString("C")</strong></p>
</body>
</html>
""";

var model = new
{
    Number = "INV-2026-001",
    CustomerName = "Ada Lovelace",
    Items = new[]
    {
        new { Description = "Implementation work", Total = 250.00m },
        new { Description = "Support", Total = 40.00m }
    },
    GrandTotal = 290.00m
};

var result = await html2Pdf.GeneratePdfAsync(
    template,
    model,
    PdfOptionsHelper.Invoice,
    HtmlOptionsHelper.Invoice);
```

### Generate from a URL

```csharp
using Nast.Html2Pdf.Models;

var result = await html2Pdf.GeneratePdfFromUrlAsync(
    "https://example.com/report",
    new PdfOptions
    {
        Format = "Letter",
        PrintBackground = true,
        WaitForImages = true,
        TimeoutMs = 30000
    });
```

### Non-DI Usage

```csharp
using Nast.Html2Pdf;
using Nast.Html2Pdf.Services;

var html2Pdf = Html2PdfFactory.Create(new BrowserPoolOptions
{
    MaxInstances = 2,
    AcquireTimeoutSeconds = 15
});

var result = await html2Pdf.GeneratePdfFromHtmlAsync("<html><body><h1>Hello</h1></body></html>");
```

## PDF and HTML Configuration

Relevant models and helpers:

- `PdfOptions`, `PdfMargins`, `PdfHeaderFooter`
- `HtmlGenerationOptions`
- `PdfOptionsHelper` presets like `A4Standard`, `Invoice`, `Report`
- `HtmlOptionsHelper` presets like `Standard`, `Invoice`, `Report`

Example:

```csharp
using Nast.Html2Pdf.Models;

var pdfOptions = new PdfOptions
{
    Format = "A4",
    Landscape = false,
    PrintBackground = true,
    Margins = new PdfMargins
    {
        Top = "2cm",
        Bottom = "2cm",
        Left = "1.5cm",
        Right = "1.5cm"
    },
    Header = new PdfHeaderFooter
    {
        Template = "<div style='font-size:10px;width:100%;text-align:center;'>Quarterly Summary</div>",
        Height = "1cm"
    }
};

var htmlOptions = new HtmlGenerationOptions
{
    BasePath = AppContext.BaseDirectory,
    InlineStyles = true,
    IncludeViewport = true,
    AdditionalCss = "body { color: #222; }"
};
```

## Testing and CI

- Keep tests tightly scoped when touching pool behavior or documentation assertions.
- Hosted Linux CI may require disabling the Chromium sandbox for the test process only.
- This repository uses `HTML2PDF_TEST_DISABLE_SANDBOX=true` in GitHub Actions test runs so browser-based tests can execute on hosted runners.
- That CI opt-in does not change the library default: production code still keeps the sandbox enabled unless `DisableSandbox` is explicitly set.

Examples:

```bash
dotnet test Tests/Nast.Html2Pdf.Tests.csproj --filter "FullyQualifiedName~BrowserPoolTests"
```

```bash
dotnet test Tests/Nast.Html2Pdf.Tests.csproj --filter "FullyQualifiedName~ProductionHardeningReleaseTests"
```

## Documentation Links

- [Quick Start](QUICK_START.md)
- [Comprehensive Guide](COMPREHENSIVE_GUIDE.md)
- [Changelog](CHANGELOG.md)
- [NuGet package](https://www.nuget.org/packages/Nast.Html2Pdf)
- [GitHub issues](https://github.com/NastMz/Html2Pdf/issues)

## License

[MIT](LICENSE)
