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

                // Create new page
                var page = await CreateNewPageAsync();
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

        private async Task<PooledPage> CreateNewPageAsync()
        {
            if (_browser == null)
            {
                await InitializeBrowserAsync();
            }

            var page = await _browser!.NewPageAsync();
            var pooledPage = new PooledPage(page, this);

            _allPages.TryAdd(page.Url, pooledPage);
            return pooledPage;
        }

        private async Task InitializeBrowserAsync()
        {
            _logger.LogDebug("Initializing browser");

            // Ensure browser is downloaded (auto-download on first use)
            await new BrowserFetcher().DownloadAsync();

            var launchOptions = new LaunchOptions
            {
                Headless = _options.Headless,
                Args = _options.AdditionalArgs
            };

            _browser = await Puppeteer.LaunchAsync(launchOptions);

            _logger.LogDebug("Browser initialized successfully");
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
                _semaphore?.Dispose();

                // Close all pages and the browser
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var page in _allPages.Values)
                        {
                            await ClosePageAsync(page);
                        }

                        if (_browser != null)
                        {
                            await _browser.CloseAsync();
                            _browser = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing browser pool");
                    }
                });

                _logger.LogDebug("Browser pool disposed");
            }
        }
    }
}
