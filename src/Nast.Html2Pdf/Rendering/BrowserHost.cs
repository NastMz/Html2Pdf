using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Diagnostics;
using PuppeteerSharp;

namespace Nast.Html2Pdf.Rendering;

/// <summary>
/// Owns the Chromium process. Starts it on demand (single flight), replaces it after
/// <see cref="BrowserOptions.MaxConversionsPerBrowser"/> conversions or when it dies, and guarantees that every process
/// and profile directory it creates is removed, including when startup and shutdown overlap.
/// </summary>
internal sealed partial class BrowserHost : IAsyncDisposable
{
    internal const int MinimumSupportedMajorVersion = 120;
    private const string ProfileDirectoryName = "nast-html2pdf";

    private readonly BrowserOptions _options;
    private readonly ILogger _logger;
    private readonly Html2PdfMetrics _metrics;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly List<BrowserInstance> _retiring = [];
    private readonly List<Task> _abandonedLaunches = [];
    private readonly string _profileRoot;
    private BrowserInstance? _current;
    private bool _disposed;
    private int _generation;

    public BrowserHost(BrowserOptions options, ILogger logger, Html2PdfMetrics metrics)
    {
        if (options.LaunchTimeout <= TimeSpan.Zero || options.MaxConversionsPerBrowser <= 0)
        {
            throw new ArgumentException("Browser.LaunchTimeout and Browser.MaxConversionsPerBrowser must be positive.");
        }

        _options = options;
        _logger = logger;
        _metrics = metrics;
        var tempBase = options.TemporaryDirectory ?? Path.GetTempPath();
        if (!Path.IsPathFullyQualified(tempBase))
        {
            throw new ArgumentException("Browser.TemporaryDirectory must be an absolute path.");
        }

        _profileRoot = Path.Combine(tempBase, ProfileDirectoryName);
    }

    public string? BrowserVersion { get; private set; }

    public string? ExecutablePath { get; private set; }

    public DateTimeOffset? LastFailureAt { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _current is { IsUsable: true };
            }
        }
    }

    internal int? CurrentProcessId
    {
        get
        {
            lock (_sync)
            {
                return _current?.ProcessId;
            }
        }
    }

    internal int OpenInstanceCount
    {
        get
        {
            lock (_sync)
            {
                return (_current is null ? 0 : 1) + _retiring.Count;
            }
        }
    }

    public async Task<BrowserLease> AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_current is { IsUsable: true } current)
                {
                    current.AddReference();
                    if (++current.Conversions >= _options.MaxConversionsPerBrowser)
                    {
                        RetireLocked(current, "conversion limit reached");
                    }

                    return new BrowserLease(this, current);
                }

                if (_current is not null)
                {
                    RetireLocked(_current, "browser is no longer connected");
                }
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            try
            {
                await _launchLock.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw ShuttingDown();
            }

            try
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (_current is { IsUsable: true })
                    {
                        continue;
                    }
                }

                var instance = await LaunchAsync(linked.Token, cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    if (!_disposed)
                    {
                        _current = instance;
                        continue;
                    }
                }

                // Shutdown started while the browser was launching: never leave it running.
                await instance.CloseAsync(_logger).ConfigureAwait(false);
                throw ShuttingDown();
            }
            finally
            {
                _launchLock.Release();
            }
        }
    }

    /// <summary>Starts the browser if it is not running.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            lease.Instance.Conversions--;
        }
    }

    /// <summary>Retires a browser that misbehaved (for example a context that could not be closed) and kills it once idle.</summary>
    public void MarkUnhealthy(BrowserInstance instance, string reason)
    {
        LastFailureAt = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            instance.Unhealthy = true;
            if (ReferenceEquals(_current, instance))
            {
                RetireLocked(instance, reason);
            }
        }
    }

    internal void Release(BrowserInstance instance)
    {
        bool close;
        lock (_sync)
        {
            close = instance.ReleaseReference() == 0 && instance.Retired && !instance.Closing;
            if (close)
            {
                instance.Closing = true;
            }
        }

        if (close)
        {
            _ = CloseRetiredAsync(instance);
        }
    }

    private void RetireLocked(BrowserInstance instance, string reason)
    {
        if (ReferenceEquals(_current, instance))
        {
            _current = null;
        }

        if (instance.Retired)
        {
            return;
        }

        instance.Retired = true;
        _retiring.Add(instance);
        Log.BrowserRetired(_logger, instance.Generation, reason);
        if (instance.References == 0 && !instance.Closing)
        {
            instance.Closing = true;
            _ = CloseRetiredAsync(instance);
        }
    }

    private async Task CloseRetiredAsync(BrowserInstance instance)
    {
        try
        {
            await instance.CloseAsync(_logger).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                _retiring.Remove(instance);
            }
        }
    }

    private async Task<BrowserInstance> LaunchAsync(CancellationToken cancellationToken, CancellationToken callerToken)
    {
        var started = Stopwatch.GetTimestamp();
        var executable = ChromiumLocator.Locate(_options);
        CleanupStaleProfiles();
        var profile = Path.Combine(_profileRoot, $"{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
        CreatePrivateDirectory(profile);

        var launchOptions = new LaunchOptions
        {
            ExecutablePath = executable,
            Headless = true,
            // Pipe transport avoids exposing a DevTools TCP port. It is used where it is verified: on macOS,
            // PuppeteerSharp's pipe transport stalled while processing protocol messages in CI, so macOS and Windows use
            // the default WebSocket transport bound to 127.0.0.1.
            Pipe = OperatingSystem.IsLinux(),
            UserDataDir = profile,
            Args = LaunchArguments(_options.DisableSandbox),
            Timeout = (int)_options.LaunchTimeout.TotalMilliseconds,
            ProtocolTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds,
            DefaultViewport = new ViewPortOptions { Width = 1024, Height = 1366 },
            EnqueueTransportMessages = true,
        };

        IBrowser? browser = null;
        Task<IBrowser>? launch = null;
        try
        {
            // Run on the thread pool: the launcher can block synchronously (process start, transport setup), and the
            // launch timeout must apply even then. An abandoned launch is closed when it eventually completes.
            launch = Task.Run(() => Puppeteer.LaunchAsync(launchOptions), CancellationToken.None);
            browser = await launch.WaitAsync(_options.LaunchTimeout, cancellationToken).ConfigureAwait(false);
            var version = await browser.GetVersionAsync().WaitAsync(_options.LaunchTimeout, cancellationToken).ConfigureAwait(false);
            var major = ParseMajorVersion(version);
            if (major < MinimumSupportedMajorVersion)
            {
                throw new PdfConversionException(PdfConversionError.RendererUnavailable,
                    $"The Chromium version ({version}) is not supported. Version {MinimumSupportedMajorVersion} or later is required.");
            }

            var instance = new BrowserInstance(browser, profile, Interlocked.Increment(ref _generation));
            browser.Disconnected += (_, _) =>
            {
                instance.Disconnected = true;
                LastFailureAt = instance.ClosingRequested ? LastFailureAt : DateTimeOffset.UtcNow;
            };

            BrowserVersion = version;
            ExecutablePath = executable;
            _metrics.BrowserLaunched(true, Stopwatch.GetElapsedTime(started));
            Log.BrowserStarted(_logger, instance.Generation, version, instance.ProcessId ?? 0);
            return instance;
        }
        catch (Exception ex)
        {
            LastFailureAt = DateTimeOffset.UtcNow;
            _metrics.BrowserLaunched(false, Stopwatch.GetElapsedTime(started));
            if (browser is not null)
            {
                await new BrowserInstance(browser, profile, 0).CloseAsync(_logger).ConfigureAwait(false);
            }
            else if (launch is { IsCompleted: false })
            {
                // We stopped waiting, but the launch continues; close whatever it eventually produces.
                AbandonLaunch(launch, profile);
            }
            else
            {
                BrowserInstance.DeleteProfile(profile);
            }

            if (ex is PdfConversionException)
            {
                throw;
            }

            if (ex is OperationCanceledException && callerToken.IsCancellationRequested)
            {
                throw;
            }

            if (_disposeCts.IsCancellationRequested)
            {
                throw ShuttingDown();
            }

            Log.BrowserLaunchFailed(_logger, ex.GetType().Name);
            Log.BrowserLaunchFailureDetail(_logger, ex);
            var hint = ex.Message.Contains("sandbox", StringComparison.OrdinalIgnoreCase)
                ? " The sandbox could not be initialized; see the deployment documentation (Browser.DisableSandbox)."
                : ex is TimeoutException ? " The browser did not start within Browser.LaunchTimeout." : string.Empty;
            throw new PdfConversionException(PdfConversionError.RendererUnavailable, "Chromium failed to start." + hint);
        }
    }

    private void AbandonLaunch(Task<IBrowser> launch, string profile)
    {
        var cleanup = launch.ContinueWith(
            async completed =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    await new BrowserInstance(completed.Result, profile, 0).CloseAsync(_logger).ConfigureAwait(false);
                }
                else
                {
                    BrowserInstance.DeleteProfile(profile);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
        lock (_sync)
        {
            _abandonedLaunches.Add(cleanup);
        }

        _ = cleanup.ContinueWith(
            t =>
            {
                lock (_sync)
                {
                    _abandonedLaunches.Remove(t);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    internal static string[] LaunchArguments(bool disableSandbox)
    {
        var args = new List<string>
        {
            // Network backstop: every request is answered through interception. Anything that bypasses it (WebSocket,
            // WebRTC, workers, service workers, popups) is sent to an unresolvable proxy and fails, and every host name
            // resolution inside Chromium fails.
            "--proxy-server=http://egress-blocked.html2pdf.invalid:9",
            // Quoted: on Unix the pipe launcher passes arguments through a shell.
            "--proxy-bypass-list=\"<-loopback>\"",
            "--host-resolver-rules=\"MAP * ~NOTFOUND\"",
            "--force-webrtc-ip-handling-policy=disable_non_proxied_udp",
            "--webrtc-ip-handling-policy=disable_non_proxied_udp",
            "--disable-features=Translate,MediaRouter,OptimizationHints,AutofillServerCommunication,DialMediaRouteProvider,InterestFeedContentSuggestions,HttpsUpgrades,AcceptCHFrame",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-domain-reliability",
            "--disable-client-side-phishing-detection",
            "--disable-breakpad",
            "--disable-sync",
            "--no-pings",
            "--deny-permission-prompts",
            "--disable-dev-shm-usage",
            "--font-render-hinting=none",
            "--no-first-run",
            "--no-default-browser-check",
        };
        if (disableSandbox)
        {
            args.Add("--no-sandbox");
        }

        return [.. args];
    }

    internal static int ParseMajorVersion(string version)
    {
        var match = VersionPattern().Match(version);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Removes profiles left behind by processes that no longer exist (for example after a crash).</summary>
    private void CleanupStaleProfiles()
    {
        try
        {
            if (!Directory.Exists(_profileRoot))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(_profileRoot))
            {
                var name = Path.GetFileName(directory);
                var dash = name.IndexOf('-', StringComparison.Ordinal);
                if (dash <= 0 || !int.TryParse(name[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == Environment.ProcessId)
                {
                    continue;
                }

                if (!ProcessExists(pid))
                {
                    BrowserInstance.DeleteProfile(directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static bool ProcessExists(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw ShuttingDown();
        }
    }

    private static PdfConversionException ShuttingDown() => new(PdfConversionError.ShuttingDown, "The converter is shutting down.");

    public async ValueTask DisposeAsync()
    {
        List<BrowserInstance> toClose;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            toClose = [.. _retiring];
            if (_current is not null)
            {
                toClose.Add(_current);
                _current = null;
            }
        }

        await _disposeCts.CancelAsync().ConfigureAwait(false);

        // Wait for an in-flight launch to observe cancellation; it closes its own browser.
        if (await _launchLock.WaitAsync(_options.LaunchTimeout + TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            _launchLock.Release();
        }

        await Task.WhenAll(toClose.Select(i => i.CloseAsync(_logger))).ConfigureAwait(false);
        Task[] abandoned;
        lock (_sync)
        {
            _retiring.Clear();
            abandoned = [.. _abandonedLaunches];
        }

        try
        {
            // Launches we stopped waiting for finish within Chromium's own launch timeout; their cleanup closes them.
            await Task.WhenAll(abandoned).WaitAsync(_options.LaunchTimeout + TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Bounded: shutdown does not wait forever for a launch that never completes.
        }

        _disposeCts.Dispose();
    }

    [GeneratedRegex(@"/(\d+)\.")]
    private static partial Regex VersionPattern();
}

/// <summary>A running browser process with its private profile.</summary>
internal sealed class BrowserInstance(IBrowser browser, string profileDirectory, int generation)
{
    private int _references;
    private int _closed;

    public IBrowser Browser { get; } = browser;

    public int Generation { get; } = generation;

    public int? ProcessId { get; } = TryGetProcessId(browser);

    public int Conversions { get; set; }

    public bool Retired { get; set; }

    public bool Closing { get; set; }

    public bool ClosingRequested => Volatile.Read(ref _closed) == 1;

    public bool Unhealthy { get; set; }

    public bool Disconnected { get; set; }

    public bool IsUsable => !Retired && !Unhealthy && !Disconnected && Browser.IsConnected;

    public int References => Volatile.Read(ref _references);

    public void AddReference() => Interlocked.Increment(ref _references);

    public int ReleaseReference() => Interlocked.Decrement(ref _references);

    public async Task CloseAsync(ILogger logger)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        Process? process = null;
        try
        {
            process = Browser.Process;
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            if (!Unhealthy && Browser.IsConnected)
            {
                await Browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.BrowserCloseFailed(logger, Generation, ex.GetType().Name);
        }

        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or NotSupportedException)
        {
            Log.BrowserCloseFailed(logger, Generation, ex.GetType().Name);
        }

        try
        {
            Browser.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Disposal of an already closed connection may throw; the process is already gone.
        }

        await DeleteProfileAsync(profileDirectory).ConfigureAwait(false);
    }

    private static async Task DeleteProfileAsync(string directory)
    {
        // Bounded (about 20 s in total): on Windows, files of an exited Chromium process can stay locked briefly.
        // Anything still left is removed by the next converter that starts with the same temporary directory.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (DeleteProfile(directory))
            {
                return;
            }

            await Task.Delay(Math.Min(2000, 100 * (attempt + 1))).ConfigureAwait(false);
        }
    }

    internal static bool DeleteProfile(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int? TryGetProcessId(IBrowser browser)
    {
        try
        {
            return browser.Process?.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>A reference to a browser held for the duration of one conversion attempt.</summary>
internal sealed class BrowserLease(BrowserHost host, BrowserInstance instance) : IDisposable
{
    private int _released;

    public BrowserInstance Instance { get; } = instance;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            host.Release(Instance);
        }
    }
}
