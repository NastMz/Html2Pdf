# Security model

Nast.Html2Pdf treats every conversion as potentially hostile. Documents, template data, remote pages and anything they
load may be attacker-controlled; the application configuring the converter is trusted. This document describes the
trust boundaries, how each guarantee is enforced, how it is verified, and the conditions it depends on.

## Trust levels

| Input | Trust | Can execute | Can reach |
|---|---|---|---|
| `Html2PdfOptions` (converter configuration) | Trusted (application) | — | Defines all policy |
| `PdfConversionOptions` (per job) | Trusted (application) | Readiness expression via DevTools | Cannot change security policy |
| Liquid template text, partials, header/footer templates | Trusted (application) | Liquid only (no .NET code, no method calls) | — |
| Template model values | **Untrusted** | Nothing: always HTML-encoded data | — |
| `TrustedHtml` model values | Trusted by explicit choice of the application | Markup as written | — |
| HTML documents, local files, remote pages and their sub-resources | **Untrusted** | Scripts only if `AllowScripts` | Only what the policy allows |

Security settings exist only on the converter. A document cannot grant itself network, file or script permissions, and
a per-job option cannot widen them.

## Network isolation

**Guarantee.** Chromium never performs network I/O itself. Every request a document makes (main document, frames,
images, styles, fonts, media, `fetch`/XHR, redirects) is intercepted and answered by the library:

1. *In-memory documents and authorized local files* are served directly (see below).
2. *Remote requests* are permitted only when the URL scheme is `https` (or `http` with `AllowHttp`), the host matches
   `AllowedHosts`, the port is in `AllowedPorts`, the method is `GET`/`HEAD`, and the URL carries no credentials. The
   library resolves the host itself and refuses the request if **any** resolved address is loopback, private, link-local
   (including `169.254.169.254` and other cloud metadata endpoints), carrier-grade NAT, multicast, reserved, a
   documentation range, or an IPv6 form embedding one of those (IPv4-mapped, NAT64, 6to4), unless it falls inside
   `AllowedPrivateNetworks`. The connection is then made to the validated address, so DNS rebinding between check and
   connect is not possible.
3. *Redirects* are returned to Chromium, which issues a new request for the target; that request goes through the same
   checks. Every hop is validated.
4. *Everything else* (`file:`, `ftp:`, `chrome:`, unknown schemes, other local origins) is refused.

Requests to remote hosts carry only `Accept`, `Accept-Language`, `User-Agent` and `Origin`. No cookies, `Authorization`,
`Referer`, client certificates, proxy settings or credentials of the hosting process are used, and `Set-Cookie` and
`Content-Disposition` responses are not passed to the browser. Response bodies are limited by `MaxResponseBytes`.

**Backstop.** Some browser features bypass request interception (WebSocket, WebRTC, workers, service workers, popups).
Chromium is started with all host-name resolution mapped to failure, an unreachable proxy for every scheme including
loopback, and WebRTC restricted to proxied UDP. Anything that escapes interception therefore fails to connect. Popups are
also closed as soon as they are created.

**Verification.** `SecurityTests` run documents with scripts enabled that attempt every vector above (stylesheets,
images, frames, objects, media, forms, `fetch`, XHR, beacons, WebSocket, EventSource, workers, service workers, WebRTC,
popups, navigation, IPv6-mapped literals) against a loopback HTTP server and a raw TCP listener, and assert that **zero**
requests and connections arrive. Further tests cover hosts resolving to loopback and private addresses (via `localhost`
and `/etc/hosts` entries), redirects to metadata endpoints, other ports, unlisted hosts and `file:`, and the absence of
cookies, `Authorization` and `Referer` headers on permitted requests.

## Local file confinement

**Guarantee.** With `LocalFiles.AllowedRoots` empty (the default) no file can be read. Otherwise, files are readable only
when their canonical path is inside a canonical root:

- Roots are canonicalized at startup by resolving every symbolic link and junction.
- Each root is exposed to documents as a distinct virtual origin (`http://r0.files.html2pdf.invalid/…`); relative
  references resolve against the document's directory and root-relative references (`/assets/x.png`) against its root.
- URL path segments are decoded individually; `.`/`..`, encoded separators, NUL and (on Windows) device names, alternate
  data streams and trailing dots/spaces are rejected.
- The resulting path is canonicalized again, checked against the root on a path-segment boundary, opened, and the path
  of the **open handle** is verified once more (`/proc/self/fd` on Linux, `F_GETPATH` on macOS,
  `GetFinalPathNameByHandle` on Windows), defeating a symbolic link swapped in between check and use.
- Input paths (`FromFile`, `FromTemplateFile`, base directories) must be absolute. Relative paths are rejected instead
  of being resolved against the process working directory.
- Local access is granted only to conversions whose source is local or names a base directory. Remote documents and
  HTML without a base directory cannot read local files even when roots are configured.
- Errors never contain absolute paths, and paths outside the roots are denied without revealing whether they exist.

**Boundaries.** Hard links and bind mounts are indistinguishable from regular files to any path-based check. Do not place
hard links or mounts to sensitive content inside an allowed root, and do not let untrusted parties write into allowed
roots. Opening a FIFO inside a root can block a conversion until its deadline.

**Verification.** `LocalFileGuardTests` and `SecurityTests` cover traversal (`../`, percent-encoded and backslash
variants), symbolic links to files and directories outside the root, symbolically linked roots, `file:` URLs, other
roots' origins, remote documents referencing local origins, and leaking of paths in errors.

## Script execution

**Guarantee.** With `AllowScripts = false` (the default) scripts are disabled in the renderer through the DevTools
protocol, and every mediated response additionally carries `Content-Security-Policy: script-src 'none'; object-src 'none';
worker-src 'none'`, which also covers frames and inline event handlers. Readiness expressions supplied by the
application run through the DevTools protocol and are unaffected.

With `AllowScripts = true`, scripts run with the same network and file restrictions. A script cannot navigate the page
away from the document: navigations that reach the network are answered with `204 No Content`, and if the document is
replaced anyway (for example by navigating to `about:blank`) the conversion fails instead of printing the replacement.

## Template injection

**Guarantee.** Templates are Liquid (Fluid), which cannot run .NET code or call methods. Model values are always
HTML-encoded on output and are never parsed as template syntax, including inside partials, headers and footers. The
`raw` filter is removed; the only way to insert markup through data is a `TrustedHtml` value created by application
code. Unknown filters and undefined values are errors, and execution steps and partial nesting are bounded.

**Your responsibility.** HTML encoding protects HTML text and quoted attribute contexts. Do not place untrusted values
into `<script>`, `<style>`, event-handler attributes, unquoted attributes or URL attributes such as `href` without
validating them; untrusted URLs could, for example, become clickable links in the PDF.

**Verification.** `TemplateRendererTests` and `SecurityTests` render payloads such as `{{ Secret }}`, `{% include %}`,
`{% raw %}`, `<script>` and attribute break-outs through documents, partials, headers and footers, with scripts enabled,
and assert they appear as text.

## Isolation between conversions

**Guarantee.** Each conversion attempt runs in a new, private browser context (separate cookies, local and session
storage, IndexedDB, cache and service-worker registrations) that is closed when the attempt ends, whatever the outcome. If
a context cannot be closed, its browser process is retired and terminated rather than reused. Documents are served from
per-operation virtual URLs, and responses are matched to the operation that requested them, so concurrent conversions
cannot receive each other's documents or output. Browser profiles are private (`0700` on Unix) per process and deleted
on shutdown; profiles left by crashed processes are removed on the next start.

**Verification.** `IsolationTests` run a probe document with scripts enabled that reads and writes cookies,
`localStorage`, `sessionStorage` and IndexedDB on the **same origin** sequentially, concurrently (12 in parallel) and
after a failed conversion, and assert it never observes another conversion's state or output.

## Information disclosure

Exception messages and log events contain operation identifiers, error categories, durations and *redacted* resource
identifiers only: `scheme://host[:port]/…/last-segment` for remote URLs (no credentials, intermediate path, query string
or fragment) and root-relative paths for local files. Document content, model values and absolute host paths are never
included. Raw exception details from the browser layer are logged only at `Trace` level, which should be treated as
sensitive. `SecurityTests.Errors_and_logs_do_not_leak_content_models_secrets_or_paths` asserts this for representative
failures with logging enabled at `Debug`.

## Conditions the guarantees depend on

- **Chromium sandbox.** The renderer sandbox is enabled by default. It requires unprivileged user namespaces on Linux
  (Ubuntu 23.10+ restricts them through AppArmor; allow them for the service account or use a container runtime that
  permits them) and cannot be used when running as root. `Browser.DisableSandbox = true` exists for such environments,
  but **without the sandbox a renderer exploit runs with the privileges of your process and can bypass every policy in
  this document**. Only disable it inside an environment that provides equivalent isolation (a dedicated, unprivileged,
  network-restricted container).
- **DevTools channel.** On Linux the library talks to Chromium over a private pipe; no TCP port is opened. On Windows
  and macOS it uses a DevTools WebSocket bound to `127.0.0.1` on a random port (PuppeteerSharp's pipe transport is not
  reliable on macOS), which other processes on the same machine could reach; run conversions under a dedicated account
  or machine when local users are untrusted.
- **Browser currency.** Rendering untrusted content safely depends on a patched Chromium. Keep it updated (see
  [operations](operations.md#keeping-chromium-current)); the library refuses versions older than 120 but cannot know
  whether a newer one is fully patched.
- **Outbound proxies.** The library connects to remote hosts directly and does not use system or environment proxies.
  Environments that require a proxy for egress cannot use remote resources with this version.
- **Host-level controls.** The library does not limit CPU, memory or disk for Chromium beyond its own capacity limits.
  Apply OS or container limits for strict resource isolation.

## Reporting vulnerabilities and updates

See [SECURITY.md](../SECURITY.md).
