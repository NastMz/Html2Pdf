# Changelog

## 2.0.0

A ground-up redesign. See [docs/migration-v2.md](docs/migration-v2.md) for the API mapping and behaviour changes.

### Added

- `Html2PdfConverter` / `IHtml2PdfConverter` with a single conversion model for HTML, Liquid templates, template files,
  local HTML files and URLs (`PdfSource`), and in-memory, stream and atomic file outputs.
- Mediated networking: every browser request is answered by the library under an explicit host, scheme, port and address
  policy, with DNS resolved and validated at connect time and every redirect hop re-checked; a browser-level backstop
  blocks anything that bypasses interception.
- Local file confinement to configured roots with symbolic-link canonicalization and post-open handle verification.
- Per-conversion browser contexts, scripts disabled by default (renderer setting plus Content-Security-Policy), downloads
  denied, popups closed, navigation away from the document refused.
- Liquid templates (Fluid) with HTML-encoded model output, `TrustedHtml`, trusted partials, strict undefined values and
  filters, and execution limits.
- Job-authoritative page geometry, custom sizes and units (`Length`, `PaperSize`, `PageMargins`), header/footer bands in
  reserved space with page numbers and totals and overflow detection, readiness conditions, resource failure policy.
- Stable `PdfConversionError` categories with redacted messages, operation identifiers, stage timings and warnings.
- Bounded admission (slots, queue length, queue timeout), whole-operation deadlines, infrastructure-only retries,
  browser recycling, graceful shutdown, health snapshots, metrics, traces and structured logs.
- Package consumer verification (including offline), soak and performance gates, CI on Linux, Windows and macOS, and a
  release workflow that publishes only the verified artifact.

### Removed

- RazorLight templates, automatic browser downloads, `AdditionalArgs`, `AllowInsecureCertificates`, the browser pool
  abstractions, `Html2PdfFactory`, `PdfResult`, and the 1.x exception hierarchy.

## 1.0.1

- Production hardening of the 1.x RazorLight/PuppeteerSharp implementation (superseded by 2.0.0).
