using System.Globalization;

namespace Nast.Html2Pdf;

/// <summary>Converter-wide configuration: renderer, security policy, capacity and timeouts.</summary>
/// <remarks>All properties are bindable from configuration. Options are validated when the converter is created.</remarks>
public sealed class Html2PdfOptions
{
    /// <summary>Chromium provisioning and process settings.</summary>
    public BrowserOptions Browser { get; set; } = new();

    /// <summary>Concurrency and queueing limits.</summary>
    public CapacityOptions Capacity { get; set; } = new();

    /// <summary>Remote network access policy. Disabled by default.</summary>
    public NetworkOptions Network { get; set; } = new();

    /// <summary>Local file access policy. No directories are authorized by default.</summary>
    public LocalFileOptions LocalFiles { get; set; } = new();

    /// <summary>Template engine settings.</summary>
    public TemplateEngineOptions Templates { get; set; } = new();

    /// <summary>
    /// Whether document scripts may run. Default: <see langword="false"/>. When disabled, scripts are blocked both by the
    /// renderer and by a <c>Content-Security-Policy</c> on every mediated document. Enabling scripts does not relax the
    /// network or file policies.
    /// </summary>
    public bool AllowScripts { get; set; }

    /// <summary>Deadline applied to conversions that do not specify <see cref="PdfConversionOptions.Timeout"/>. Default: 60 seconds.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum number of attempts for a conversion when the renderer fails for infrastructure reasons (for example the
    /// browser process exits). Invalid input, denied operations, missing resources and timeouts are never retried. Default: 2.
    /// </summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>
    /// How long <see cref="Html2PdfConverter.ShutdownAsync"/> lets accepted conversions finish before cancelling them. Default: 30 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Chromium provisioning and process settings.</summary>
public sealed class BrowserOptions
{
    /// <summary>Environment variable consulted when <see cref="ExecutablePath"/> is not set.</summary>
    public const string ExecutablePathEnvironmentVariable = "NAST_HTML2PDF_CHROMIUM_PATH";

    /// <summary>
    /// Path to a Chrome, Chromium or chrome-headless-shell executable. When not set, the
    /// <c>NAST_HTML2PDF_CHROMIUM_PATH</c> environment variable is used, then (if <see cref="SearchStandardLocations"/>)
    /// the standard installation locations of the operating system. The library never downloads a browser.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Whether standard installation locations are searched when no path is configured. Default: <see langword="true"/>.</summary>
    public bool SearchStandardLocations { get; set; } = true;

    /// <summary>
    /// Disables the Chromium sandbox. Default: <see langword="false"/>. Only enable this when the operating environment
    /// cannot provide the sandbox (for example running as root, or in containers without user namespaces) and the
    /// environment itself provides equivalent isolation: without the sandbox, a renderer compromise is a compromise of
    /// the hosting process's user account.
    /// </summary>
    public bool DisableSandbox { get; set; }

    /// <summary>Maximum time to start the browser. Default: 30 seconds.</summary>
    public TimeSpan LaunchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// After this many conversions the browser process is replaced (after its in-flight work finishes), which bounds
    /// long-running memory growth. Default: 250.
    /// </summary>
    public int MaxConversionsPerBrowser { get; set; } = 250;

    /// <summary>
    /// Directory under which the library creates per-process browser profiles. Each profile is private to one browser
    /// process and deleted when it closes. Default: the system temporary directory.
    /// </summary>
    public string? TemporaryDirectory { get; set; }
}

/// <summary>Concurrency and queueing limits. All limits are finite.</summary>
public sealed class CapacityOptions
{
    /// <summary>Maximum conversions rendering at the same time. Default: the smaller of 4 and the processor count.</summary>
    public int MaxConcurrentConversions { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 4);

    /// <summary>
    /// Maximum conversions waiting for a rendering slot. Further requests fail immediately with
    /// <see cref="PdfConversionError.CapacityExceeded"/>. Default: 32.
    /// </summary>
    public int MaxQueueLength { get; set; } = 32;

    /// <summary>
    /// Maximum time a conversion waits for a rendering slot before failing with
    /// <see cref="PdfConversionError.CapacityExceeded"/>. The conversion deadline also applies. Default: 30 seconds.
    /// </summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Remote network policy. Every request a document makes is mediated by the library; Chromium itself has no
/// direct network access. Nothing remote is reachable until hosts are listed in <see cref="AllowedHosts"/>.
/// </summary>
public sealed class NetworkOptions
{
    /// <summary>
    /// Whether <see cref="PdfSource.FromUrl(Uri)"/> may be used. Default: <see langword="false"/>. The URL's host must also be in
    /// <see cref="AllowedHosts"/>.
    /// </summary>
    public bool AllowUrlSources { get; set; }

    /// <summary>
    /// Hosts that documents and URL sources may contact: exact names (<c>cdn.example.com</c>), subdomain wildcards
    /// (<c>*.example.com</c>, which does not match <c>example.com</c> itself) or IP literals. Empty by default.
    /// </summary>
    public IList<string> AllowedHosts { get; set; } = new List<string>();

    /// <summary>Whether plain <c>http</c> is allowed in addition to <c>https</c>. Default: <see langword="false"/>.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Ports that may be contacted. Default: 443 and 80 (80 only takes effect when <see cref="AllowHttp"/> is set).</summary>
    public IList<int> AllowedPorts { get; set; } = new List<int> { 443, 80 };

    /// <summary>
    /// Non-public address ranges (CIDR notation, for example <c>10.20.0.0/16</c>) that allowed hosts may resolve to.
    /// By default only public unicast addresses are reachable: loopback, private, link-local (including cloud metadata
    /// endpoints), carrier-grade NAT, multicast and reserved ranges are refused after DNS resolution and on every redirect.
    /// </summary>
    public IList<string> AllowedPrivateNetworks { get; set; } = new List<string>();

    /// <summary>Maximum size of a single remote response body. Default: 20 MiB.</summary>
    public long MaxResponseBytes { get; set; } = 20 * 1024 * 1024;
}

/// <summary>Local file policy.</summary>
public sealed class LocalFileOptions
{
    /// <summary>
    /// Absolute paths of directories from which documents, templates and their resources may be read. Empty by default,
    /// which disables local file access. Paths are canonicalized (symbolic links resolved) when the converter is created
    /// and must exist. Each root is exposed to documents as its own origin, so root-relative URLs such as
    /// <c>/assets/logo.png</c> resolve against the root that contains the document.
    /// </summary>
    public IList<string> AllowedRoots { get; set; } = new List<string>();

    /// <summary>Maximum size of a single local file. Default: 50 MiB.</summary>
    public long MaxFileBytes { get; set; } = 50 * 1024 * 1024;
}

/// <summary>Template engine settings.</summary>
public sealed class TemplateEngineOptions
{
    /// <summary>Culture used by formatting filters. Default: <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>Maximum number of template execution steps, bounding runaway loops. Default: 1,000,000.</summary>
    public int MaxSteps { get; set; } = 1_000_000;

    /// <summary>Maximum nesting depth of partials. Default: 16.</summary>
    public int MaxRecursion { get; set; } = 16;
}
