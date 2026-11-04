# Migrating from 1.x to 2.0

Version 2 is a redesign. The conversion use cases of 1.x (template + model, template file, raw HTML, URL, write to file or
stream, DI or factory) are all supported, but the API, the template language, the defaults and the error model changed.
There is no compatibility mode: the 1.x behaviours that changed were unsafe or unreliable (see
[why](#why-these-changes)).

## API mapping

| 1.x | 2.0 |
|---|---|
| `services.AddHtml2Pdf(o => …)` / `AddHtml2PdfWithLifetime(…)` | `services.AddHtml2Pdf(o => …)` registers a singleton `Html2PdfConverter` and `IHtml2PdfConverter` |
| `Html2PdfFactory.Create(...)` / `CreateDefault()` | `new Html2PdfConverter(new Html2PdfOptions { … })`; dispose it (`await using`) |
| `IHtml2PdfService` | `IHtml2PdfConverter` |
| `GeneratePdfAsync(template, model, pdfOptions, htmlOptions)` | `ConvertAsync(PdfSource.FromTemplate(template, model), options)` |
| `GeneratePdfFromFileAsync(templatePath, model, …)` | `ConvertAsync(PdfSource.FromTemplateFile(templatePath, model), options)` (path must be absolute and under `LocalFiles.AllowedRoots`) |
| `GeneratePdfFromHtmlAsync(html, pdfOptions)` | `ConvertAsync(PdfSource.FromHtml(html, baseDirectory: null), options)` |
| `GeneratePdfFromUrlAsync(url, pdfOptions)` | `ConvertAsync(PdfSource.FromUrl(url), options)` with `Network.AllowUrlSources = true` and the host in `Network.AllowedHosts` |
| `GeneratePdf…ToFileAsync(…)` returning `bool` | `ConvertToFileAsync(source, path, overwrite, options)` returning `PdfFileResult`; atomic, never overwrites unless asked |
| `GeneratePdfToStreamAsync(…)` returning `Stream?` | `ConvertToStreamAsync(source, destination, options)` writing to your stream |
| `PdfResult { Success, Data, ErrorMessage, Exception }` | Success returns `PdfDocument { Content, OperationId, Timings, Warnings }`; failure throws `PdfConversionException { Error, OperationId }` |
| `Html2PdfException`, `TemplateException`, `BrowserPoolException`, … | `PdfConversionException` with a `PdfConversionError` category; cancellation is `OperationCanceledException` |
| `Html2PdfDiagnostics` | `GetHealth()`, the `Nast.Html2Pdf` meter and activity source, structured logs |
| `IHtmlGenerator`, `IPdfConverter`, `IBrowserPool`, `IPooledPage` | Removed (internal concerns) |

## Options mapping

| 1.x `PdfOptions` | 2.0 `PdfConversionOptions` |
|---|---|
| `Format = "A4"` (unknown names silently became A4) | `PaperSize = PaperSize.A4` / `PaperSize.FromName("Letter")` (unknown names throw); `Ledger` is `Tabloid` + `Landscape` |
| `Width`, `Height` (inches, as `float`) | `PaperSize = PaperSize.Custom(Length.Inches(8), Length.Inches(4))` (any unit) |
| `Landscape = true` | `Orientation = PageOrientation.Landscape` |
| `Margins = new PdfMargins { Top = "1cm", … }` | `Margins = new PageMargins("1cm")` or `new PageMargins(top, right, bottom, left)`; default is 10mm, as before |
| `Header`/`Footer = new PdfHeaderFooter { Template, Data, Height }` | `new PdfHeaderFooter(template, height)` plus `HeaderFooterModel`; the margin must fit the band |
| `PrintBackground`, `Scale`, `PageRanges` | Same names (`Scale` is `double`) |
| `WaitForImages` | Always on (including lazy images and web fonts) |
| `TimeoutMs` | `Timeout` (`TimeSpan`), covering the whole operation |

| 1.x `BrowserPoolOptions` | 2.0 `Html2PdfOptions` |
|---|---|
| `MinInstances`, `MaxInstances`, `AcquireTimeoutSeconds` | `Capacity.MaxConcurrentConversions`, `Capacity.MaxQueueLength`, `Capacity.QueueTimeout` (one browser process, isolated contexts per conversion) |
| `MaxLifetimeMinutes` | `Browser.MaxConversionsPerBrowser` |
| `ExecutablePath` | `Browser.ExecutablePath` or `NAST_HTML2PDF_CHROMIUM_PATH` |
| `SkipBrowserDownload = false` (default: download on first use) | Removed: the browser is never downloaded; install it with your image |
| `BrowserCacheDirectory` | Removed (no downloads); `Browser.TemporaryDirectory` controls profile location |
| `DisableSandbox` | `Browser.DisableSandbox` |
| `AllowInsecureCertificates` | Removed: TLS certificates are always validated |
| `AdditionalArgs` | Removed: arbitrary flags could disable the security model |
| `Headless` | Removed: always headless |

| 1.x `HtmlGenerationOptions` | 2.0 |
|---|---|
| `BasePath` | `baseDirectory` of `FromHtml` / `TemplateSourceOptions.BaseDirectory` |
| `AdditionalCss` | Put the CSS in the template or document |
| `AdditionalJs` | Removed; scripts are off unless `AllowScripts` |
| `Encoding`, `IncludeViewport`, `InlineStyles` | Removed: documents are UTF-8; layout is print-based |

## Templates: Razor to Liquid

1.x compiled templates as Razor (C#) with RazorLight. 2.0 uses Liquid:

| Razor (1.x) | Liquid (2.0) |
|---|---|
| `@Model.Customer.Name` | `{{ Customer.Name }}` |
| `@foreach (var item in Model.Items) { <tr>…@item.Name…</tr> }` | `{% for item in Items %}<tr>…{{ item.Name }}…</tr>{% endfor %}` |
| `@if (Model.Paid) { … } else { … }` | `{% if Paid %}…{% else %}…{% endif %}` |
| `@item.Total.ToString("N2")` | `{{ item.Total \| format_number: 'N2' }}` |
| `@Model.Date.ToString("yyyy-MM-dd")` | `{{ Date \| format_date: 'yyyy-MM-dd' }}` |
| `@Html.Raw(Model.Body)` | Pass `new TrustedHtml(body)` in the model and write `{{ Body }}` |
| `@await Html.PartialAsync(...)`, layouts | `{% render 'name' %}` with `TemplateSourceOptions.Partials` |
| Arbitrary C# expressions and method calls | Not available: compute values in your model |

If you prefer to keep Razor, render HTML yourself (for example with ASP.NET Core's `HtmlRenderer` for Razor components,
which compiles templates at build time) and pass it to `PdfSource.FromHtml`.

## Behaviour changes to plan for

- **No network by default.** Documents that load fonts, images or styles from the Internet fail with `AccessDenied`
  until their hosts are listed in `Network.AllowedHosts` (or the resources are embedded locally).
- **No local files by default.** Template files, local documents and relative resources need `LocalFiles.AllowedRoots`.
- **No scripts by default.** Charts or content generated by JavaScript need `AllowScripts = true` and usually a readiness
  condition (`Readiness.WaitForExpression` or `WaitForSelector`).
- **Missing resources fail.** 1.x printed documents with broken images. 2.0 fails unless
  `ResourceFailures = ResourceFailurePolicy.BestEffort`, which returns warnings instead.
- **Job layout wins over CSS `@page`.** 1.x let a document's `@page` size override the requested format. Set
  `LayoutPrecedence = PageLayoutPrecedence.Document` to keep that behaviour.
- **Undefined template values fail** instead of rendering empty.
- **Header/footer problems fail** instead of printing the raw template or overlapping the body; the header height must
  fit within the top margin (likewise for footers).
- **Errors are exceptions.** Replace `if (result.Success)` checks with `try`/`catch (PdfConversionException)`.

## Why these changes

1.x silently downloaded Chromium on first use, accepted arbitrary browser flags, allowed documents to reach any network
destination (including cloud metadata endpoints) and the local file system, executed document scripts, compiled every
template as fresh C# code (unbounded memory growth and full process privileges for templates), reported success with
missing images or unrendered header templates, and returned raw exception messages that could contain document data.
Those behaviours are incompatible with treating conversion as a hostile operation, so they were removed rather than
kept behind compatibility switches.
