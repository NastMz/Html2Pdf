using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Browser pool for performance optimization
    /// </summary>
    internal sealed class BrowserPool : IBrowserPool
    {
        private readonly BrowserPoolOptions _options;
        private readonly ILogger<BrowserPool> _logger;
        private readonly ConcurrentQueue<PooledPage> _availablePages;
        private readonly ConcurrentDictionary<string, PooledPage> _allPages;
        private readonly SemaphoreSlim _semaphore;
        private readonly Timer _cleanupTimer;
        private IBrowser? _browser;
        private bool _disposed;

        public BrowserPool(BrowserPoolOptions options, ILogger<BrowserPool> logger)
        {
            _options = options;
            _logger = logger;
            _availablePages = new ConcurrentQueue<PooledPage>();
            _allPages = new ConcurrentDictionary<string, PooledPage>();
            _semaphore = new SemaphoreSlim(_options.MaxInstances, _options.MaxInstances);

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
                        availablePage.MarkAsInUse();
                        _logger.LogDebug("Reusing existing browser page");
                        return availablePage;
                    }
                    else
                    {
                        // Invalid page, close it and create a new one
                        await ClosePageAsync(availablePage);
                    }
                }

                // Create new page with retry logic
                var page = await CreateNewPageWithRetryAsync();
                page.MarkAsInUse();

                _logger.LogDebug("Created new browser page in {Duration}ms", stopwatch.ElapsedMilliseconds);
                return page;
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
                await InitializeBrowserAsync();
            }

            var page = await _browser!.NewPageAsync();
            var pooledPage = new PooledPage(page, this);

            _allPages.TryAdd(page.Url, pooledPage);
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
                // Ensure browser is downloaded (auto-download on first use)
                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();

                var defaultArgs = new List<string>
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-web-security",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-renderer-backgrounding",
                    "--disable-features=VizDisplayCompositor",
                    "--disable-ipc-flooding-protection",
                    "--disable-blink-features=AutomationControlled"
                };

                var allArgs = defaultArgs.Concat(_options.AdditionalArgs).ToArray();

                var launchOptions = new LaunchOptions
                {
                    Headless = _options.Headless,
                    Args = allArgs,
                    Timeout = 30000 // 30 second timeout
                };

                _browser = await Puppeteer.LaunchAsync(launchOptions);

                _logger.LogDebug("Browser initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize browser");
                throw new BrowserPoolException("Failed to initialize browser", ex);
            }
        }

        private bool IsPageValid(PooledPage page)
        {
            if (page.Page.IsClosed)
                return false;

            // Check if the page has exceeded its lifetime
            var age = DateTime.UtcNow - page.CreatedAt;
            if (age.TotalMinutes > _options.MaxLifetimeMinutes)
                return false;

            return true;
        }

        private async Task ClosePageAsync(PooledPage page)
        {
            try
            {
                if (!page.Page.IsClosed)
                {
                    await page.Page.CloseAsync();
                }

                _allPages.TryRemove(page.Page.Url, out _);
                _logger.LogDebug("Closed expired browser page");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing browser page");
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

        /// <summary>
        /// Force closes all browser processes - used for cleanup in tests
        /// </summary>
        public async Task ForceCloseAllBrowsersAsync()
        {
            try
            {
                await CloseAllPagesAsync();
                await CloseBrowserAsync();
                await ForceKillChromeProcessesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during force close of browsers");
            }
        }

        private async Task CloseAllPagesAsync()
        {
            foreach (var page in _allPages.Values)
            {
                await ClosePageAsync(page);
            }
            _allPages.Clear();
            
            // Clear the available pages queue
            while (_availablePages.TryDequeue(out var _))
            {
                // Intentionally empty - just clearing the queue
            }
        }

        private async Task CloseBrowserAsync()
        {
            if (_browser != null && !_browser.IsClosed)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
        }

        private async Task ForceKillChromeProcessesAsync()
        {
            await Task.Run(() =>
            {
                var chromeProcessNames = new[] { "chrome", "chromium", "chromium-browser" };
                foreach (var processName in chromeProcessNames)
                {
                    KillProcessesByName(processName);
                }
            });
        }

        private void KillProcessesByName(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000); // Wait up to 5 seconds
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error killing {ProcessName} process {ProcessId}", processName, process.Id);
                }
                finally
                {
                    process.Dispose();
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
                    var disposeTask = Task.Run(async () =>
                    {
                        await ForceCloseAllBrowsersAsync();
                    });

                    // Wait for disposal to complete with timeout
                    if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        _logger.LogWarning("Browser pool disposal timed out, forcing cleanup");
                        // Force kill any remaining processes
                        ForceKillChromeProcessesSync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during browser pool disposal");
                    ForceKillChromeProcessesSync();
                }
                finally
                {
                    _semaphore?.Dispose();
                }

                _logger.LogDebug("Browser pool disposed");
            }
        }

        private void ForceKillChromeProcessesSync()
        {
            try
            {
                var chromeProcessNames = new[] { "chrome", "chromium", "chromium-browser" };
                foreach (var processName in chromeProcessNames)
                {
                    KillProcessesByName(processName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error force killing chrome processes");
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
                    await ForceCloseAllBrowsersAsync().WaitAsync(cts.Token);
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Browser pool async disposal timed out, forcing cleanup");
                    ForceKillChromeProcessesSync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing browser pool asynchronously");
                    ForceKillChromeProcessesSync();
                }
                finally
                {
                    _semaphore?.Dispose();
                }

                _logger.LogDebug("Browser pool disposed asynchronously");
            }
        }
    }
}
