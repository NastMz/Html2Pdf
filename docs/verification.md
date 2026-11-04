# Verification report

This document records what has been verified for Nast.Html2Pdf 2.0.0, how, and under which conditions. Everything here is
reproducible with the commands at the end; CI runs the same checks on every change.

## Approach

- **Real components only.** Integration tests drive the public API against a real Chromium process. Nothing in the
  rendering, network or file path is mocked. Two internal fault-injection hooks exist, used only to reach instants that
  cannot be targeted from outside: immediately after admission and immediately before a file commit.
- **Independent inspection.** PDFs are parsed with [PdfPig](https://github.com/UglyToad/PdfPig) in strict mode (text,
  word positions, page sizes, link annotations, images, fonts from the font dictionaries, bookmarks) and rasterized with
  PDFium through [Docnet](https://github.com/GowenGit/docnet) to check pixels at known millimetre positions. Neither
  shares code with Chromium's PDF writer. Layout is checked with geometric assertions (band edges, margins, colours at
  positions) rather than golden images, so tests are stable across font and anti-aliasing differences.
- **Adversarial network tests.** Every denial is backed by a loopback HTTP server or raw TCP listener that counts
  requests; a test only passes if nothing was contacted.
- **The shipped artifact.** The NuGet package is packed, its contents checked against an allowlist, and installed through
  an isolated NuGet cache into a separate consumer application, which is run on each supported runtime, including
  inside a network namespace without Internet access.

## Reference environment

All results below were produced in this environment unless stated otherwise:

| | |
|---|---|
| OS | Ubuntu 24.04.5 LTS, Linux x64 (container) |
| Hardware | 4 vCPU, 15 GiB RAM |
| Browser | Chrome for Testing 153.0.8010.12 |
| .NET | 8.0.33 runtime; 10.0.14 runtime (SDK 10.0.403) |

## Results

### Test suite (`tests/Nast.Html2Pdf.Tests`)

220 tests excluding the soak test: 159 unit tests and 61 real-browser integration tests.

| Run | Result |
|---|---|
| net8.0 and net10.0, as root (sandbox disabled, as Chromium requires for root) | 220/220 passed on both |
| net8.0 as an unprivileged user **with the Chromium sandbox enabled** (the production default) | all passed; process inspection during the run showed renderers under the zygote sandbox and no process with `--no-sandbox` |
| Repeated full runs (flakiness check) | 5 consecutive net8.0 runs and 3 consecutive runs with net8.0 and net10.0 executing simultaneously (double load): all green after fixing the issues listed below |

Coverage by area:

| Area | Examples of what is asserted |
|---|---|
| Rendering fidelity | Invoice template: searchable text, Unicode (accented Latin, em dash, `☕`), link annotations, table order, margins, printed backgrounds. 220-row report: 4+ pages, rows in order, appendix page break, header and `Page n of N` footer on every page, bookmarks. Paper sizes A3/A4 landscape/Legal/custom; scale; print vs screen media; page ranges; lazy images; local web font embedded (`+DejaVuSans` subset in the font dictionaries); CSS backgrounds from root-relative URLs |
| Layout precedence | Job geometry overrides a document's `@page { size: A5 landscape; margin: 0 }`; document precedence honours it |
| Header/footer bands | Pixel-measured band edges at exactly `margin − height` … `margin` (±0.6 mm) on first and middle pages; body never overlaps; overflow, external images, scripts and undefined values fail |
| Network policy | Zero requests from 20+ vectors with scripts enabled; loopback/private resolution via `localhost` and hosts-file names refused; redirects to metadata, other ports, unlisted hosts and `file:` refused; permitted requests carry no cookies, `Authorization` or `Referer` |
| File confinement | Traversal variants, symbolic links to files and directories, linked roots, `file:` URLs, other roots, remote documents referencing local origins, relative input paths |
| Scripts and navigation | Scripts, inline handlers and frame scripts do not run by default; run when allowed; navigation away is refused or fails rather than printing other content |
| Template injection | Liquid syntax, `<script>`, attribute break-outs and page-number markup in model values print as text in body, partials, headers and footers |
| Isolation | Cookies, localStorage, sessionStorage and IndexedDB on the same origin: sequential, 12 concurrent, and after a failed conversion |
| Disclosure | Errors and Debug-level logs contain no document text, model values, signed query strings or absolute paths |
| Lifecycle | Cancellation before admission, while queued, during preparation, while rendering and at the commit point; deadlines in loading vs readiness; existing files preserved on every failure; no temporary files left; stream failures; browser killed mid-render (retried and recovered; or failed alone when retries are disabled); missing browser; 1 ms launch timeout (no orphaned processes); shutdown during startup, during rendering with queued work, and graceful drain; six create/dispose cycles; broken logging and metrics listeners |
| Concurrency | 10 simultaneous requests against 2 slots + 3 queue entries: exactly 5 complete and 5 are rejected immediately; the test server never sees more than 2 concurrent document loads. Expired queue entries do not lose or duplicate slots. 36 mixed jobs (successes, denials, template errors, readiness failures, invalid options, slow resources) each get their expected outcome |

### Soak (`Category=Soak`)

Mixed workload (invoice templates, multi-page reports with footers, local files, permitted remote images, denied
resources, template errors, readiness timeouts), 8 callers on 4 slots, browser recycled every 100 conversions:

| Run | Iterations | Outcomes | Chromium processes | Chromium RSS | Profiles | Managed heap |
|---|---|---|---|---|---|---|
| 1 | 600 | 344 success, 86 denied, 85 template errors, 85 readiness/deadline failures (as designed) | 7–12 | 665–1139 MB, no upward trend | ≤ 1 | 5.6 → 8.1 MB |
| 2 | 600 | identical distribution | 7–11 | 668–1093 MB, no upward trend | ≤ 1 | 5.7 → 8.8 MB |

After shutdown: 0 Chromium processes, 0 profile directories, 0 active operations. The test asserts these bounds.

### Package and platform verification (`eng/verify-package.sh`)

| Check | Result |
|---|---|
| Package contents (allowlist: assembly, XML docs, README, LICENSE, nuspec) | Pass; no browser binaries or stray files |
| Package metadata | Version, repository URL and source commit recorded in the nuspec |
| Consumer on .NET 8 (installed from the packed `.nupkg`) | 18/18 checks: DI and explicit lifetime, HTML, template, template file, local file and URL sources, file/stream outputs, overwrite protection, denials, template and layout errors, cancellation, health, shutdown |
| Consumer on .NET 10 | 18/18 |
| Both consumers inside a network namespace with Internet confirmed unreachable | 18/18 each |

### Performance (`tools/Nast.Html2Pdf.Benchmarks`)

Three consecutive runs at concurrency 4:

| Metric | Run 1 | Run 2 | Run 3 | Release gate |
|---|---|---|---|---|
| Cold start, median (new converter, first invoice) | 451 ms | 509 ms | 440 ms | ≤ 3000 ms |
| Warm invoice p50 / p95 | 173 / 215 ms | 191 / 242 ms | 187 / 215 ms | ≤ 750 / 1200 ms |
| Warm 400-row report p50 | 278 ms | 285 ms | 280 ms | ≤ 1500 ms |
| Throughput (200 mixed documents) | 6.47 docs/s | 6.71 docs/s | 6.60 docs/s | ≥ 2.0 docs/s |
| Service time under saturation p95 | 832 ms | 751 ms | 794 ms | ≤ 3500 ms |
| Peak RSS (.NET process + Chromium tree) | 1581 MB | 1550 MB | 1602 MB | ≤ 3000 MB |

The gates (`eng/perf-thresholds.json`) leave headroom for shared CI runners and are enforced by CI; a deliberately
impossible threshold was confirmed to fail the gate.

### Dependencies

`dotnet list package --vulnerable --include-transitive` reports no known vulnerabilities for the library's dependency
graph (PuppeteerSharp 25.15.0, Fluid.Core 2.41.1, Microsoft.Extensions.* 8.0.x). CI fails on any high or critical finding.

## Defects found and fixed during verification

The adversarial and soak tests found real defects in the new implementation before release, all fixed and covered by
regression tests:

- Chromium startup over the pipe transport failed because an argument (`<-loopback>`) was interpreted by a shell.
- A navigation to `about:blank` (which never reaches the network) could replace the document; aborting a blocked
  navigation committed an error page. Navigations are now answered with `204 No Content`, and the document is verified
  before and after printing.
- A redirect to `file:` was refused inside Chromium without a failed mediated request, so a conversion could succeed
  with a missing image. Browser-side request failures are now recorded.
- Pages opened with `window.open` could stall printing; they are now closed immediately.
- A timed-out browser launch left orphaned Chromium processes (8 in the test); abandoned launches are now cleaned up.
- A rare DevTools race during page creation (~0.3% of conversions under load) failed conversions; it is now retried in
  a fresh context without discarding the healthy browser.
- The header/footer measurement page treated the browser's favicon request as a required resource.

## Not verified here, and residual limitations

- **Windows and macOS** were not available in the reference environment. The CI workflow runs the complete test suite
  and package verification on `windows-latest` and `macos-latest` with the current stable Chrome; consult the CI run for
  this revision for those results. Platform-specific code paths (handle-path verification via `GetFinalPathNameByHandle`
  and `F_GETPATH`, Windows path rules) are exercised only there. The offline consumer run uses Linux network namespaces
  and is Linux-only.
- **ARM64** has not been tested.
- **Visual fidelity** is verified geometrically, not pixel-for-pixel; text metrics differ between font installations.
- **Chromium versions** other than the reference version are exercised only through CI's current stable channel.
- **Not provided:** PDF/A, PDF/UA conformance, digital signatures, encryption, WebSockets/workers/popups for script-driven
  documents, outbound HTTP proxies. Hard links and bind mounts inside allowed roots cannot be detected (see
  [security](security.md)).

## Reproducing

```bash
# Test suite (Chromium on PATH or NAST_HTML2PDF_CHROMIUM_PATH set; hosts entries as in .github/workflows/ci.yml)
dotnet test tests/Nast.Html2Pdf.Tests -c Release --filter "Category!=Soak"
```

```bash
HTML2PDF_SOAK_ITERATIONS=600 HTML2PDF_SOAK_REPORT=artifacts/soak.json dotnet test tests/Nast.Html2Pdf.Tests -c Release -f net8.0 --filter "Category=Soak"
```

```bash
eng/verify-package.sh --offline required
```

```bash
dotnet run -c Release --project tools/Nast.Html2Pdf.Benchmarks -- --thresholds eng/perf-thresholds.json --report artifacts/perf.json
```
