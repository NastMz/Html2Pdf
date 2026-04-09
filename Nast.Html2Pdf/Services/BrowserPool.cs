using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Helpers;
using PuppeteerSharp;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Exceptions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Nast.Html2Pdf.Services
{
    /// <summary>
    /// Browser pool configuration options
    /// </summary>
    public sealed class BrowserPoolOptions
    {
        /// <summary>
        /// Minimum number of browser instances
        /// </summary>
        public int MinInstances { get; set; } = 1;

        /// <summary>
        /// Maximum number of browser instances
        /// </summary>
        public int MaxInstances { get; set; } = 5;

        /// <summary>
        /// Maximum lifetime of an instance (in minutes)
        /// </summary>
        public int MaxLifetimeMinutes { get; set; } = 60;

        /// <summary>
        /// Maximum wait time to acquire an instance (in seconds)
        /// </summary>
        public int AcquireTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Additional arguments for the browser
        /// </summary>
        public string[] AdditionalArgs { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Run in headless mode
        /// </summary>
        public bool Headless { get; set; } = true;

        /// <summary>
        /// Optional Chromium executable path.
        /// </summary>
        public string? ExecutablePath { get; set; }

        /// <summary>
        /// Optional browser cache directory.
        /// </summary>
        public string? BrowserCacheDirectory { get; set; }

        /// <summary>
        /// Skip automatic browser download.
        /// </summary>
        public bool SkipBrowserDownload { get; set; }

        /// <summary>
        /// Allow insecure certificates explicitly.
        /// </summary>
        public bool AllowInsecureCertificates { get; set; }

        /// <summary>
        /// Disable Chromium sandbox explicitly.
        /// </summary>
        public bool DisableSandbox { get; set; }
    }

    /// <summary>
    /// Pooled page from the browser pool
    /// </summary>
    internal sealed class PooledPage : IPooledPage
    {
        private readonly IBrowserPool _pool;
        private readonly DateTime _createdAt;
        private bool _isInUse;
        private bool _disposed;

        public string PageId { get; } = Guid.NewGuid().ToString("N");
        public IPage Page { get; }
        public bool IsInUse => _isInUse;
        public DateTime CreatedAt => _createdAt;

        public PooledPage(IPage page, IBrowserPool pool)
        {
            Page = page;
            _pool = pool;
            _createdAt = DateTime.UtcNow;
            _isInUse = false;
        }

        public void MarkAsInUse()
        {
            _isInUse = true;
        }

        public void MarkAsAvailable()
        {
            _isInUse = false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                _ = _pool.ReturnPageAsync(this);
            }
        }
    }

    internal enum BrowserPoolMeasurementCategory
    {
        PoolLifecycle,
        Provisioning,
        BrowserInterop
    }

    internal enum BrowserPoolOperation
    {
        AcquirePage,
        ReturnPage,
        DisposePool,
        BrowserProvisioning,
        BrowserStartup,
        PageCreation,
        ClosePage,
        CloseBrowser
    }

    internal enum BrowserPoolOptimizationFocus
    {
        ConsumerRuntime,
        TestOnlyOverheadOrBrowserHostNoise
    }

    internal sealed record BrowserPoolMeasurement(
        BrowserPoolOperation Operation,
        BrowserPoolMeasurementCategory Category,
        TimeSpan Duration,
        bool Succeeded);

    internal sealed record BrowserPoolMeasurementGuidanceResult(
        BrowserPoolOperation Operation,
        BrowserPoolOptimizationFocus Focus,
        bool AffectsLibraryConsumers,
        string Rationale);

    internal sealed record BrowserPoolProvisioningPlan(
        string? ExecutablePath,
        string? BrowserCacheDirectory,
        bool ShouldDownload,
        string ProvisioningLockKey);

    internal static class BrowserPoolMeasurementGuidance
    {
        public static BrowserPoolMeasurementGuidanceResult Describe(BrowserPoolOperation operation)
        {
            return operation switch
            {
                BrowserPoolOperation.AcquirePage => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.ConsumerRuntime,
                    true,
                    "AcquirePage sits on the request path, so consumer-perceived latency and timeout behavior depend on it."),
                BrowserPoolOperation.BrowserProvisioning => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.ConsumerRuntime,
                    true,
                    "Browser provisioning is a cold-start cost that consumers feel before the first conversion can run."),
                BrowserPoolOperation.BrowserStartup => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.ConsumerRuntime,
                    true,
                    "Browser startup is real runtime overhead because consumers wait for Chromium to become usable."),
                BrowserPoolOperation.PageCreation => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.ConsumerRuntime,
                    true,
                    "Page creation happens on the active conversion path, so it directly affects consumer-visible runtime."),
                BrowserPoolOperation.ReturnPage => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise,
                    false,
                    "ReturnPage is post-request bookkeeping; it can add harness noise, but it is not the primary consumer-visible latency target."),
                BrowserPoolOperation.DisposePool => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise,
                    false,
                    "Pool disposal is teardown work that often dominates test shutdown and browser-host noise rather than steady-state consumer runtime."),
                BrowserPoolOperation.ClosePage => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise,
                    false,
                    "Closing pooled pages is cleanup noise unless repeated evidence shows it blocks the runtime path for consumers."),
                BrowserPoolOperation.CloseBrowser => new BrowserPoolMeasurementGuidanceResult(
                    operation,
                    BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise,
                    false,
                    "Closing Chromium is browser-host cleanup noise that is commonly overrepresented in tests and disposal hangs."),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
        }

        public static BrowserPoolMeasurementGuidanceResult Describe(BrowserPoolMeasurement measurement)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            return Describe(measurement.Operation);
        }
    }

    internal sealed class BrowserPoolRuntimeHooks
    {
        public Action<BrowserPoolMeasurement>? OnMeasurement { get; init; }
        public Func<BrowserPoolOptions, Task>? EnsureBrowserProvisionedAsync { get; init; }
        public Func<BrowserPoolOptions, string[], Task<IBrowser>>? LaunchBrowserAsync { get; init; }
        public Func<IBrowser, Task<IPage>>? CreatePageAsync { get; init; }
        public Func<IPage, Task>? ClosePageAsync { get; init; }
        public Func<IBrowser, Task>? CloseBrowserAsync { get; init; }

        public Task EnsureProvisionedAsync(BrowserPoolOptions options)
        {
            return EnsureBrowserProvisionedAsync?.Invoke(options) ?? DefaultEnsureBrowserProvisionedAsync(options);
        }

        public Task<IBrowser> LaunchAsync(BrowserPoolOptions options, string[] launchArguments)
        {
            return LaunchBrowserAsync?.Invoke(options, launchArguments) ?? DefaultLaunchBrowserAsync(options, launchArguments);
        }

        public Task<IPage> CreatePageAsyncCore(IBrowser browser)
        {
            return CreatePageAsync?.Invoke(browser) ?? browser.NewPageAsync();
        }

        public Task ClosePageAsyncCore(IPage page)
        {
            return ClosePageAsync?.Invoke(page) ?? page.CloseAsync();
        }

        public Task CloseBrowserAsyncCore(IBrowser browser)
        {
            return CloseBrowserAsync?.Invoke(browser) ?? browser.CloseAsync();
        }

        internal static async Task DefaultEnsureBrowserProvisionedAsync(BrowserPoolOptions options)
        {
            var plan = BrowserPool.BuildProvisioningPlan(options);
            if (!plan.ShouldDownload)
            {
                return;
            }

            var provisioningLock = BrowserPool.ProvisioningLocks.GetOrAdd(plan.ProvisioningLockKey, _ => new SemaphoreSlim(1, 1));
            await provisioningLock.WaitAsync();

            try
            {
                var browserFetcher = string.IsNullOrWhiteSpace(plan.BrowserCacheDirectory)
                    ? new BrowserFetcher()
                    : new BrowserFetcher(new BrowserFetcherOptions { Path = plan.BrowserCacheDirectory });

                await browserFetcher.DownloadAsync();
            }
            finally
            {
                provisioningLock.Release();
            }
        }

        private static Task<IBrowser> DefaultLaunchBrowserAsync(BrowserPoolOptions options, string[] launchArguments)
        {
            var plan = BrowserPool.BuildProvisioningPlan(options);
            var launchOptions = new LaunchOptions
            {
                Headless = options.Headless,
                Args = launchArguments,
                Timeout = 30000,
                ExecutablePath = plan.ExecutablePath
            };

            return Puppeteer.LaunchAsync(launchOptions);
        }
    }

    /// <summary>
    /// Browser pool for performance optimization
    /// </summary>
    internal sealed class BrowserPool : IBrowserPool
    {
        internal const string DisableSandboxEnvironmentVariable = "HTML2PDF_TEST_DISABLE_SANDBOX";
        private readonly BrowserPoolOptions _options;
        private readonly ILogger<BrowserPool> _logger;
        private readonly ConcurrentQueue<PooledPage> _availablePages;
        private readonly ConcurrentDictionary<string, PooledPage> _allPages;
        private readonly SemaphoreSlim _semaphore;
        private readonly SemaphoreSlim _browserInitSemaphore;
        private readonly Timer _cleanupTimer;
        private readonly BrowserPoolRuntimeHooks _runtimeHooks;
        internal static readonly ConcurrentDictionary<string, SemaphoreSlim> ProvisioningLocks = new(StringComparer.OrdinalIgnoreCase);
        private IBrowser? _browser;
        private bool _disposed;

        public BrowserPool(BrowserPoolOptions options, ILogger<BrowserPool> logger)
            : this(options, logger, null)
        {
        }

        internal BrowserPool(BrowserPoolOptions options, ILogger<BrowserPool> logger, BrowserPoolRuntimeHooks? runtimeHooks)
        {
            _options = Html2PdfValidation.ValidateBrowserPoolOptions(options);
            _logger = logger;
            _runtimeHooks = runtimeHooks ?? new BrowserPoolRuntimeHooks();
            _availablePages = new ConcurrentQueue<PooledPage>();
            _allPages = new ConcurrentDictionary<string, PooledPage>();
            _semaphore = new SemaphoreSlim(_options.MaxInstances, _options.MaxInstances);
            _browserInitSemaphore = new SemaphoreSlim(1, 1);

            // Timer for periodic cleanup
            _cleanupTimer = new Timer(CleanupExpiredPages, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task<IPooledPage> GetPageAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BrowserPool));

            var timeout = TimeSpan.FromSeconds(_options.AcquireTimeoutSeconds);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                return await MeasureAsync(BrowserPoolOperation.AcquirePage, BrowserPoolMeasurementCategory.PoolLifecycle, async () =>
                {
                    // Esperar por disponibilidad en el pool
                    if (!await _semaphore.WaitAsync(timeout))
                    {
                        throw new BrowserPoolException($"Timeout waiting for available browser instance after {timeout.TotalSeconds} seconds");
                    }

                    // Try to get an available page
                    if (_availablePages.TryDequeue(out var availablePage))
                    {
                        if (IsPageValid(availablePage))
                        {
                            // Double-check validity before returning
                            if (IsPageValid(availablePage))
                            {
                                availablePage.MarkAsInUse();
                                _logger.LogDebug("Reusing existing browser page");
                                return availablePage;
                            }
                            else
                            {
                                // Page became invalid between checks, close it
                                await ClosePageAsync(availablePage);
                            }
                        }
                        else
                        {
                            // Invalid page, close it and create a new one
                            await ClosePageAsync(availablePage);
                        }
                    }

                    // Create new page with retry logic
                    var page = await CreateNewPageWithRetryAsync();

                    // Final validation before returning
                    if (!IsPageValid(page))
                    {
                        await ClosePageAsync(page);
                        throw new BrowserPoolException("Created page is invalid");
                    }

                    page.MarkAsInUse();

                    _logger.LogDebug("Created new browser page in {Duration}ms", stopwatch.ElapsedMilliseconds);
                    return page;
                });
            }
            catch (Exception ex)
            {
                _semaphore.Release();
                _logger.LogError(ex, "Error getting browser page");
                throw new BrowserPoolException("Error getting browser page", ex);
            }
        }

        public async Task ReturnPageAsync(IPooledPage page)
        {
            if (_disposed || page == null)
                return;

            try
            {
                await MeasureAsync(BrowserPoolOperation.ReturnPage, BrowserPoolMeasurementCategory.PoolLifecycle, async () =>
                {
                    var pooledPage = (PooledPage)page;
                    pooledPage.MarkAsAvailable();

                    if (IsPageValid(pooledPage))
                    {
                        _availablePages.Enqueue(pooledPage);
                        _logger.LogDebug("Returned browser page to pool");
                    }
                    else
                    {
                        await ClosePageAsync(pooledPage);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error returning browser page to pool");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<PooledPage> CreateNewPageWithRetryAsync()
        {
            const int maxRetries = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await TryCreatePageAsync();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Failed to create browser page on attempt {Attempt}/{MaxRetries}", 
                        attempt, maxRetries);

                    if (attempt < maxRetries)
                    {
                        await CleanupBrowserForRetryAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
                    }
                }
            }

            throw new BrowserPoolException($"Failed to create browser page after {maxRetries} attempts", lastException!);
        }

        private async Task<PooledPage> TryCreatePageAsync()
        {
            if (_browser == null || _browser.IsClosed)
            {
                await _browserInitSemaphore.WaitAsync();
                try
                {
                    // Double-check pattern
                    if (_browser == null || _browser.IsClosed)
                    {
                        await InitializeBrowserAsync();
                    }
                }
                finally
                {
                    _browserInitSemaphore.Release();
                }
            }

            var page = await MeasureAsync(
                BrowserPoolOperation.PageCreation,
                BrowserPoolMeasurementCategory.BrowserInterop,
                () => _runtimeHooks.CreatePageAsyncCore(_browser!));
            var pooledPage = new PooledPage(page, this);

            _allPages.TryAdd(pooledPage.PageId, pooledPage);
            return pooledPage;
        }

        private async Task CleanupBrowserForRetryAsync()
        {
            try
            {
                if (_browser != null && !_browser.IsClosed)
                {
                    await _browser.CloseAsync();
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Error cleaning up browser during retry");
            }
            finally
            {
                _browser = null;
            }
        }

        private async Task InitializeBrowserAsync()
        {
            _logger.LogDebug("Initializing browser");

            try
            {
                await MeasureAsync(
                    BrowserPoolOperation.BrowserProvisioning,
                    BrowserPoolMeasurementCategory.Provisioning,
                    () => _runtimeHooks.EnsureProvisionedAsync(_options));

                var launchArguments = BuildLaunchArguments(_options);

                _browser = await MeasureAsync(
                    BrowserPoolOperation.BrowserStartup,
                    BrowserPoolMeasurementCategory.BrowserInterop,
                    () => _runtimeHooks.LaunchAsync(_options, launchArguments));

                _logger.LogDebug("Browser initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize browser");
                throw new BrowserPoolException("Failed to initialize browser", ex);
            }
        }

        internal static BrowserPoolProvisioningPlan BuildProvisioningPlan(BrowserPoolOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var executablePath = string.IsNullOrWhiteSpace(options.ExecutablePath)
                ? null
                : options.ExecutablePath.Trim();
            var browserCacheDirectory = string.IsNullOrWhiteSpace(options.BrowserCacheDirectory)
                ? null
                : options.BrowserCacheDirectory.Trim();
            var shouldDownload = executablePath == null && !options.SkipBrowserDownload;
            var lockKey = executablePath != null
                ? $"executable:{executablePath}"
                : $"cache:{browserCacheDirectory ?? "default"}";

            return new BrowserPoolProvisioningPlan(executablePath, browserCacheDirectory, shouldDownload, lockKey);
        }

        internal static string[] BuildLaunchArguments(BrowserPoolOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var args = new List<string>
            {
                "--disable-dev-shm-usage",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-features=VizDisplayCompositor",
                "--disable-ipc-flooding-protection",
                "--disable-blink-features=AutomationControlled",
                "--disable-gpu",
                "--disable-software-rasterizer",
                "--disable-extensions",
                "--disable-default-apps",
                "--disable-sync",
                "--disable-translate",
                "--no-first-run",
                "--hide-scrollbars",
                "--mute-audio",
                "--autoplay-policy=user-gesture-required"
            };

            if (options.AllowInsecureCertificates)
            {
                args.Add("--ignore-certificate-errors");
            }

            if (options.DisableSandbox || IsDisableSandboxEnvironmentOptInEnabled())
            {
                args.Add("--no-sandbox");
                args.Add("--disable-setuid-sandbox");
            }

            if (options.AdditionalArgs.Length > 0)
            {
                args.AddRange(options.AdditionalArgs);
            }

            return args.ToArray();
        }

        private static bool IsDisableSandboxEnvironmentOptInEnabled()
        {
            return bool.TryParse(Environment.GetEnvironmentVariable(DisableSandboxEnvironmentVariable), out var disableSandbox)
                && disableSandbox;
        }

        private bool IsPageValid(PooledPage page)
        {
            try
            {
                if (page?.Page == null || page.Page.IsClosed)
                    return false;

                // Check if the page has exceeded its lifetime
                var age = DateTime.UtcNow - page.CreatedAt;
                if (age.TotalMinutes > _options.MaxLifetimeMinutes)
                    return false;

                // Additional validation: check if the page's browser is still valid
                if (page.Page.Browser == null || page.Page.Browser.IsClosed)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating page, considering it invalid");
                return false;
            }
        }

        private async Task ClosePageAsync(PooledPage page, bool swallowExceptions = true)
        {
            try
            {
                if (page.Page != null && !page.Page.IsClosed)
                {
                    await MeasureAsync(
                        BrowserPoolOperation.ClosePage,
                        BrowserPoolMeasurementCategory.BrowserInterop,
                        () => _runtimeHooks.ClosePageAsyncCore(page.Page));
                }

                _allPages.TryRemove(page.PageId, out _);
                _logger.LogDebug("Closed expired browser page");
            }
            catch (Exception ex)
            {
                _allPages.TryRemove(page.PageId, out _);
                if (swallowExceptions)
                {
                    _logger.LogWarning(ex, "Error closing browser page");
                    return;
                }

                throw;
            }
        }

        private void CleanupExpiredPages(object? state)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var expiredPages = new List<PooledPage>();

                    // Identify expired pages that are not in use
                    foreach (var page in _allPages.Values)
                    {
                        if (!page.IsInUse && !IsPageValid(page))
                        {
                            expiredPages.Add(page);
                        }
                    }

                    // Close expired pages
                    foreach (var page in expiredPages)
                    {
                        await ClosePageAsync(page);
                    }

                    if (expiredPages.Count > 0)
                    {
                        _logger.LogDebug("Cleaned up {Count} expired browser pages", expiredPages.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during browser pool cleanup");
                }
            });
        }

        public async Task ForceCloseAllBrowsersAsync()
        {
            await CloseOwnedResourcesAsync(throwOnFailure: false);
        }

        private async Task CloseOwnedResourcesAsync(bool throwOnFailure)
        {
            var cleanupExceptions = new List<Exception>();

            try
            {
                await CloseAllPagesAsync(cleanupExceptions, throwOnFailure);
                await CloseBrowserAsync(cleanupExceptions, throwOnFailure);
            }
            finally
            {
                if (throwOnFailure && cleanupExceptions.Count > 0)
                {
                    throw new AggregateException("One or more owned browser resources failed to close.", cleanupExceptions);
                }
            }
        }

        private async Task CloseAllPagesAsync(List<Exception> cleanupExceptions, bool throwOnFailure)
        {
            foreach (var page in _allPages.Values.ToArray())
            {
                try
                {
                    await ClosePageAsync(page, swallowExceptions: !throwOnFailure);
                }
                catch (Exception ex)
                {
                    cleanupExceptions.Add(ex);
                }
            }

            _allPages.Clear();

            // Clear the available pages queue
            while (_availablePages.TryDequeue(out var _))
            {
                // Intentionally empty - just clearing the queue
            }
        }

        private async Task CloseBrowserAsync(List<Exception> cleanupExceptions, bool throwOnFailure)
        {
            if (_browser != null && !_browser.IsClosed)
            {
                try
                {
                    await MeasureAsync(
                        BrowserPoolOperation.CloseBrowser,
                        BrowserPoolMeasurementCategory.BrowserInterop,
                        () => _runtimeHooks.CloseBrowserAsyncCore(_browser));
                }
                catch (Exception ex)
                {
                    if (throwOnFailure)
                    {
                        cleanupExceptions.Add(ex);
                    }
                    else
                    {
                        _logger.LogWarning(ex, "Error closing owned browser");
                    }
                }

                if (_browser != null)
                {
                    _browser = null;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;

                _cleanupTimer?.Dispose();

                // Close all pages and the browser - wait for completion
                try
                {
                    var disposeTask = Task.Run(() => MeasureAsync(
                        BrowserPoolOperation.DisposePool,
                        BrowserPoolMeasurementCategory.PoolLifecycle,
                        () => CloseOwnedResourcesAsync(throwOnFailure: false)));

                    // Wait for disposal to complete with timeout
                    if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        _logger.LogWarning("Browser pool disposal timed out while waiting for owned resources to close");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during browser pool disposal");
                }
                finally
                {
                    _semaphore?.Dispose();
                    _browserInitSemaphore?.Dispose();
                }

                _logger.LogDebug("Browser pool disposed");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;

                if (_cleanupTimer != null)
                {
                    await _cleanupTimer.DisposeAsync();
                }

                // Close all pages and the browser asynchronously with timeout
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await MeasureAsync(
                            BrowserPoolOperation.DisposePool,
                            BrowserPoolMeasurementCategory.PoolLifecycle,
                            () => CloseOwnedResourcesAsync(throwOnFailure: true))
                        .WaitAsync(cts.Token);
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Browser pool async disposal timed out while waiting for owned resources to close");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing browser pool asynchronously");
                    throw;
                }
                finally
                {
                    _semaphore?.Dispose();
                }

                _logger.LogDebug("Browser pool disposed asynchronously");
            }
        }

        private async Task MeasureAsync(
            BrowserPoolOperation operation,
            BrowserPoolMeasurementCategory category,
            Func<Task> action)
        {
            var stopwatch = Stopwatch.StartNew();
            var succeeded = false;

            try
            {
                await action();
                succeeded = true;
            }
            finally
            {
                stopwatch.Stop();
                _runtimeHooks.OnMeasurement?.Invoke(new BrowserPoolMeasurement(operation, category, stopwatch.Elapsed, succeeded));
            }
        }

        private async Task<T> MeasureAsync<T>(
            BrowserPoolOperation operation,
            BrowserPoolMeasurementCategory category,
            Func<Task<T>> action)
        {
            var stopwatch = Stopwatch.StartNew();
            var succeeded = false;

            try
            {
                var result = await action();
                succeeded = true;
                return result;
            }
            finally
            {
                stopwatch.Stop();
                _runtimeHooks.OnMeasurement?.Invoke(new BrowserPoolMeasurement(operation, category, stopwatch.Elapsed, succeeded));
            }
        }
    }
}
