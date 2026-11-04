# Operations guide

## Provisioning Chromium

The package contains no browser and never downloads one. The converter uses, in order:

1. `Html2PdfOptions.Browser.ExecutablePath`;
2. the `NAST_HTML2PDF_CHROMIUM_PATH` environment variable;
3. if `Browser.SearchStandardLocations` is true (default): `/usr/bin/chromium`, `/usr/bin/chromium-browser`,
   `/usr/bin/google-chrome`, `/usr/bin/google-chrome-stable`, `/usr/bin/chrome-headless-shell`,
   `/usr/bin/microsoft-edge` on Linux; Chrome, Chromium and Edge under Program Files / LocalAppData on Windows;
   Chrome, Chromium and Edge in `/Applications` on macOS.

Chromium 120 or later is required; older versions fail with `RendererUnavailable`. The first launch of a freshly
installed browser can be much slower than later ones (on Windows CI runners, real-time scanning pushed cold starts past
the 30 s default `Browser.LaunchTimeout`); raise the timeout or warm the browser up during deployment where that applies. The effective version is reported by
`GetHealth().BrowserVersion` and logged when the browser starts. Call `await converter.WarmUpAsync()` during application
startup so that a missing or broken browser fails deployment rather than the first request.

Install fonts for every script your documents use (for example `fonts-noto-cjk` for Chinese, Japanese and Korean, and
`fonts-noto-color-emoji`), or better, embed web fonts with `@font-face` from an allowed root so output does not depend on
the host.

### Container example (Debian-based .NET image)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends chromium fonts-dejavu-core fonts-noto-cjk \
 && rm -rf /var/lib/apt/lists/*
ENV NAST_HTML2PDF_CHROMIUM_PATH=/usr/bin/chromium
# Run as the image's non-root user; Chromium refuses to sandbox as root.
USER app
WORKDIR /app
COPY ./publish .
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

The Chromium sandbox needs to create user namespaces. Docker's default seccomp profile and some Kubernetes policies block
this; Chromium then fails to start with a sandbox error (`RendererUnavailable`, with guidance in the message). Either:

- permit user namespaces for the container (for example a seccomp profile that allows `clone`/`unshare` with
  `CLONE_NEWUSER`, on a host where unprivileged user namespaces are enabled) — **recommended**; or
- set `Browser.DisableSandbox = true` *and* compensate at the container level: non-root user, read-only root file
  system, no egress except what documents need, no secrets mounted, dedicated pod/VM. See
  [security](security.md#conditions-the-guarantees-depend-on) for what the sandbox protects.

On Ubuntu 23.10+ hosts, AppArmor restricts unprivileged user namespaces; allow them for the service (or system-wide with
`sysctl kernel.apparmor_restrict_unprivileged_userns=0`) to keep the sandbox enabled.

### Offline operation

Once Chromium and fonts are installed, HTML, template and local-file conversions need no network access. The package
verification runs the consumer application inside a network namespace without Internet access to confirm this.

### Keeping Chromium current

Chromium renders untrusted content and must be patched. Update it with your OS image at least monthly and promptly after
Chrome security releases. CI tests every change against the current stable Chrome. The library depends only on the
DevTools protocol features listed in this repository's tests, so routine browser updates do not require a library update.

## Configuration reference

| Option | Default | Notes |
|---|---|---|
| `Browser.ExecutablePath` | — | See provisioning |
| `Browser.SearchStandardLocations` | `true` | |
| `Browser.DisableSandbox` | `false` | Security-relevant; see above |
| `Browser.LaunchTimeout` | 30 s | |
| `Browser.MaxConversionsPerBrowser` | 250 | The browser is replaced after this many conversions, bounding long-run memory growth |
| `Browser.TemporaryDirectory` | system temp | Parent of private per-process profiles, deleted on shutdown |
| `Capacity.MaxConcurrentConversions` | min(4, CPUs) | Rendering slots |
| `Capacity.MaxQueueLength` | 32 | Excess requests fail immediately with `CapacityExceeded` |
| `Capacity.QueueTimeout` | 30 s | Maximum wait for a slot |
| `DefaultTimeout` | 60 s | Per-conversion deadline when `PdfConversionOptions.Timeout` is not set |
| `MaxAttempts` | 2 | Attempts for renderer infrastructure failures only |
| `ShutdownTimeout` | 30 s | Time accepted work gets to finish during shutdown |
| `AllowScripts` | `false` | |
| `Network.*` | everything off | See [security](security.md#network-isolation) |
| `LocalFiles.AllowedRoots` / `MaxFileBytes` | none / 50 MiB | |
| `Templates.Culture` / `MaxSteps` / `MaxRecursion` | invariant / 1,000,000 / 16 | |

All options are validated when the converter is created; invalid values throw `ArgumentException`.

## Capacity planning

Measured on the reference environment (4 vCPU, 16 GB, Linux, Chromium 153; see [verification](verification.md)):

| Workload | Result |
|---|---|
| Cold start (new converter, first invoice including browser launch) | ~0.45–0.5 s |
| Warm one-page invoice (template + render) | p50 ~0.17–0.19 s, p95 ~0.21–0.24 s |
| Warm ~10-page report (400 table rows) | p50 ~0.28 s |
| Mixed throughput at 4 concurrent slots | ~6.5–7 documents/s |
| Service time under saturation (excluding queue) | p95 ~0.75–0.83 s |
| Peak resident memory, .NET process plus Chromium, 4 slots | ~1.5–1.6 GB |

Rendering is CPU-bound: more slots than CPU cores mostly increase latency. Start with one slot per core (the default caps
at 4), size `MaxQueueLength` to the backlog you are willing to hold (`queue length × typical duration / slots` is the
worst-case wait), and plan roughly 300–400 MB of memory per concurrent slot plus ~300 MB base. Use several application
instances for more throughput.

## Health and readiness

`GetHealth()` returns a snapshot: `Status` (`Idle`, `Ready`, `Saturated`, `Unavailable`, `ShuttingDown`, `Stopped`),
`IsAcceptingWork`, active and queued counts, configured limits, `BrowserRunning`, `BrowserVersion`, `LastBrowserFailure`
and success/failure counters. An ASP.NET Core health check needs no extra package dependency in the library:

```csharp
public sealed class Html2PdfHealthCheck(IHtml2PdfConverter converter) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = converter.GetHealth();
        var data = new Dictionary<string, object>
        {
            ["status"] = health.Status.ToString(),
            ["active"] = health.ActiveConversions,
            ["queued"] = health.QueuedConversions,
            ["browser"] = health.BrowserVersion ?? "not started",
        };
        return Task.FromResult(health.Status switch
        {
            Html2PdfStatus.Ready or Html2PdfStatus.Idle => HealthCheckResult.Healthy(data: data),
            Html2PdfStatus.Saturated => HealthCheckResult.Degraded("Conversion queue is full", data: data),
            _ => HealthCheckResult.Unhealthy(health.Status.ToString(), data: data),
        });
    }
}
```

## Telemetry

**Metrics** (meter `Nast.Html2Pdf`):

| Instrument | Type | Tags |
|---|---|---|
| `html2pdf.conversions` | counter | `source` (html, template, template-file, file, url), `outcome` (success, failure, cancelled), `error` |
| `html2pdf.conversion.duration` | histogram (s) | same as above |
| `html2pdf.queue.duration` | histogram (s) | |
| `html2pdf.capacity.rejections` | counter | `reason` |
| `html2pdf.retries` | counter | |
| `html2pdf.browser.launches` | counter | `outcome` |
| `html2pdf.conversions.active` / `.queued` | observable up-down counters | |
| `html2pdf.browser.running` | observable up-down counter | |

**Traces:** activity source `Nast.Html2Pdf`, one `html2pdf.convert` activity per conversion with
`html2pdf.operation_id` and `html2pdf.source` tags and an error status on failure.

**Logs:** category `Nast.Html2Pdf`. Information: conversion succeeded, browser started/retired, shutdown. Warning:
conversion failed (with category and safe reason), retries, browser close problems. Error: browser failed to start.
Debug: admission and per-resource denials. Trace: raw internal exception details (may contain URLs; treat as sensitive).

Failures in logging providers or metric listeners are contained and never change a conversion's outcome.

**Retries.** A small fraction of conversions (about 0.3% in the soak test at 4 concurrent slots) hit a known DevTools
race in which a newly created page's session disappears during initialization. These are retried transparently in a new
browser context, produce a `Retried` warning on the result and are counted by `html2pdf.retries`. Persistent retries
indicate a browser or host problem.

## Shutdown

`ShutdownAsync()` (also called by `DisposeAsync`/`Dispose` and by the DI container) stops admitting work, fails queued
conversions with `ShuttingDown`, gives accepted conversions up to `ShutdownTimeout`, cancels the remainder, then closes
Chromium (terminating its process tree if it does not exit) and deletes its profile. It is idempotent and safe during
browser startup. In ASP.NET Core, keep `HostOptions.ShutdownTimeout` larger than `ShutdownTimeout` so the container can
complete disposal.

If the hosting process is killed abruptly, Chromium children may outlive it; their profiles are cleaned up by the next
converter that starts with the same `TemporaryDirectory`. Supervisors (systemd, Kubernetes) that kill the whole process
group or container avoid orphans entirely.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `RendererUnavailable: No Chromium executable was found` | Install Chrome/Chromium or configure the path |
| `RendererUnavailable: … sandbox could not be initialized` | Enable unprivileged user namespaces or see the sandbox options above |
| macOS: conversions time out while the browser starts; Chrome logs `bootstrap_look_up … MachPortRendezvousServer … Permission denied` | The Chrome app bundle's code signature is broken (for example it was copied or renamed after installation), so its helper processes cannot start. Use a normally installed, signed Chrome (`codesign --verify --deep --strict "/Applications/Google Chrome.app"`) |
| `AccessDenied: A required resource was denied by policy: https://cdn…` | Add the host to `Network.AllowedHosts`, embed the resource locally, or use `ResourceFailurePolicy.BestEffort` |
| `AccessDenied: … resolves to an address that is not a permitted destination` | An allowed host resolves to a private address; add its range to `AllowedPrivateNetworks` if intended |
| `ResourceUnavailable: … (file not found)` | Relative paths resolve against the document's directory; root-relative paths against its root |
| `InvalidRequest: Header content does not fit in its band` | Increase the header height and the top margin, or reduce the content |
| Wrong glyphs or boxes for some scripts | Install fonts for those scripts or embed web fonts |
