using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace Nast.Html2Pdf.Tests.Services
{
    [Collection("Integration")]
    public class BrowserPoolTests
    {
        private const string DisableSandboxEnvironmentVariable = "HTML2PDF_TEST_DISABLE_SANDBOX";
        private readonly Mock<ILogger<BrowserPool>> _loggerMock;
        private readonly BrowserPoolOptions _options;

        private static async Task DisposePoolAsync(BrowserPool pool, params IPooledPage?[] pages)
        {
            foreach (var page in pages)
            {
                if (page != null && page.IsInUse)
                {
                    await pool.ReturnPageAsync(page);
                }
            }

            await pool.DisposeAsync();
        }

        public BrowserPoolTests()
        {
            _loggerMock = new Mock<ILogger<BrowserPool>>();
            _options = new BrowserPoolOptions
            {
                MinInstances = 1,
                MaxInstances = 3,
                AcquireTimeoutSeconds = 5
            };
        }

        [Fact]
        public void BuildLaunchArguments_ShouldUseSecureDefaults()
        {
            using var _ = new EnvironmentVariableScope(DisableSandboxEnvironmentVariable, null);
            var args = BrowserPool.BuildLaunchArguments(new BrowserPoolOptions());

            args.ShouldContain("--disable-dev-shm-usage");
            args.ShouldContain("--disable-gpu");
            args.ShouldContain("--no-first-run");
            args.ShouldNotContain("--ignore-certificate-errors");
            args.ShouldNotContain("--disable-web-security");
            args.ShouldNotContain("--no-sandbox");
            args.ShouldNotContain("--disable-setuid-sandbox");
        }

        [Fact]
        public void BuildLaunchArguments_ShouldAddOnlyExplicitInsecureOptInsAndAdditionalArgs()
        {
            using var _ = new EnvironmentVariableScope(DisableSandboxEnvironmentVariable, null);
            var args = BrowserPool.BuildLaunchArguments(new BrowserPoolOptions
            {
                AllowInsecureCertificates = true,
                DisableSandbox = true,
                AdditionalArgs = ["--window-size=1280,720"]
            });

            args.ShouldContain("--ignore-certificate-errors");
            args.ShouldContain("--no-sandbox");
            args.ShouldContain("--disable-setuid-sandbox");
            args.ShouldContain("--window-size=1280,720");
            args.ShouldNotContain("--disable-web-security");
        }

        [Fact]
        public void BuildLaunchArguments_ShouldEnableSandboxByEnvironmentOptInForBrowserTests()
        {
            using var _ = new EnvironmentVariableScope(DisableSandboxEnvironmentVariable, "true");

            var args = BrowserPool.BuildLaunchArguments(new BrowserPoolOptions());

            args.ShouldContain("--no-sandbox");
            args.ShouldContain("--disable-setuid-sandbox");
        }

        [Fact]
        public void BuildLaunchArguments_ShouldIgnoreFalseEnvironmentOptInForBrowserTests()
        {
            using var _ = new EnvironmentVariableScope(DisableSandboxEnvironmentVariable, "false");

            var args = BrowserPool.BuildLaunchArguments(new BrowserPoolOptions());

            args.ShouldNotContain("--no-sandbox");
            args.ShouldNotContain("--disable-setuid-sandbox");
        }

        [Fact]
        public void BuildProvisioningPlan_ShouldPreferExplicitExecutableAndSkipDownloads()
        {
            var plan = BrowserPool.BuildProvisioningPlan(new BrowserPoolOptions
            {
                ExecutablePath = @"C:\tools\chrome.exe",
                BrowserCacheDirectory = @"C:\browser-cache",
                SkipBrowserDownload = false
            });

            plan.ExecutablePath.ShouldBe(@"C:\tools\chrome.exe");
            plan.BrowserCacheDirectory.ShouldBe(@"C:\browser-cache");
            plan.ShouldDownload.ShouldBeFalse();
            plan.ProvisioningLockKey.ShouldContain("executable:");
        }

        [Fact]
        public void Constructor_WithInvalidOptions_ShouldThrowValidationError()
        {
            Should.Throw<ArgumentOutOfRangeException>(() =>
                new BrowserPool(new BrowserPoolOptions
                {
                    MinInstances = 2,
                    MaxInstances = 1,
                    AcquireTimeoutSeconds = 5
                }, _loggerMock.Object));
        }

        [Fact]
        public async Task DisposeAsync_ShouldCloseEveryOwnedPageEvenWhenTrackedUrlsMatch()
        {
            var browserMock = CreateBrowserMock();
            var page1 = CreatePageMock(browserMock.Object, "about:blank");
            var page2 = CreatePageMock(browserMock.Object, "about:blank");
            var pageQueue = new Queue<IPage>(new[] { page1.Object, page2.Object });
            var closedPages = 0;
            var closedBrowsers = 0;

            await using var pool = new BrowserPool(
                new BrowserPoolOptions { MaxInstances = 2, AcquireTimeoutSeconds = 1 },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult(browserMock.Object),
                    CreatePageAsync = _ => Task.FromResult(pageQueue.Dequeue()),
                    ClosePageAsync = _ =>
                    {
                        closedPages++;
                        return Task.CompletedTask;
                    },
                    CloseBrowserAsync = _ =>
                    {
                        closedBrowsers++;
                        return Task.CompletedTask;
                    }
                });

            var pooledPage1 = await pool.GetPageAsync();
            var pooledPage2 = await pool.GetPageAsync();
            await pool.ReturnPageAsync(pooledPage1);
            await pool.ReturnPageAsync(pooledPage2);

            await pool.DisposeAsync();

            closedPages.ShouldBe(2);
            closedBrowsers.ShouldBe(1);
        }

        [Fact]
        public async Task GetPageAsync_ShouldAssignStableDistinctInternalPageIdsForPagesSharingTheSameUrl()
        {
            var browserMock = CreateBrowserMock();
            var page1 = CreatePageMock(browserMock.Object, "about:blank");
            var page2 = CreatePageMock(browserMock.Object, "about:blank");
            var pageQueue = new Queue<IPage>(new[] { page1.Object, page2.Object });

            await using var pool = new BrowserPool(
                new BrowserPoolOptions { MaxInstances = 2, AcquireTimeoutSeconds = 1 },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult(browserMock.Object),
                    CreatePageAsync = _ => Task.FromResult(pageQueue.Dequeue()),
                    ClosePageAsync = _ => Task.CompletedTask,
                    CloseBrowserAsync = _ => Task.CompletedTask
                });

            var pooledPage1 = await pool.GetPageAsync();
            var firstPageId = ((PooledPage)pooledPage1).PageId;
            await pool.ReturnPageAsync(pooledPage1);

            var reacquiredPage = await pool.GetPageAsync();
            var reacquiredPageId = ((PooledPage)reacquiredPage).PageId;
            var pooledPage2 = await pool.GetPageAsync();
            var secondPageId = ((PooledPage)pooledPage2).PageId;

            reacquiredPage.ShouldBeSameAs(pooledPage1);
            reacquiredPageId.ShouldBe(firstPageId);
            secondPageId.ShouldNotBe(firstPageId);
            ((PooledPage)pooledPage2).Page.Url.ShouldBe("about:blank");
        }

        [Fact]
        public async Task DisposeAsync_WhenOneOwnedPageFailsToClose_ShouldContinueAndSurfaceAggregateFailure()
        {
            var browserMock = CreateBrowserMock();
            var page1 = CreatePageMock(browserMock.Object, "about:blank");
            var page2 = CreatePageMock(browserMock.Object, "about:blank");
            var pageQueue = new Queue<IPage>(new[] { page1.Object, page2.Object });
            var closePageCalls = 0;
            var closeBrowserCalls = 0;

            await using var pool = new BrowserPool(
                new BrowserPoolOptions { MaxInstances = 2, AcquireTimeoutSeconds = 1 },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult(browserMock.Object),
                    CreatePageAsync = _ => Task.FromResult(pageQueue.Dequeue()),
                    ClosePageAsync = _ =>
                    {
                        closePageCalls++;
                        if (closePageCalls == 1)
                        {
                            throw new InvalidOperationException("close-page-failed");
                        }

                        return Task.CompletedTask;
                    },
                    CloseBrowserAsync = _ =>
                    {
                        closeBrowserCalls++;
                        return Task.CompletedTask;
                    }
                });

            var pooledPage1 = await pool.GetPageAsync();
            var pooledPage2 = await pool.GetPageAsync();
            await pool.ReturnPageAsync(pooledPage1);
            await pool.ReturnPageAsync(pooledPage2);

            var exception = await Should.ThrowAsync<AggregateException>(async () => await pool.DisposeAsync().AsTask());

            exception.InnerExceptions.Count.ShouldBe(1);
            exception.InnerExceptions[0].Message.ShouldContain("close-page-failed");
            closePageCalls.ShouldBe(2);
            closeBrowserCalls.ShouldBe(1);
        }

        [Fact]
        public async Task GetPageAsync_ShouldReturnPooledPage()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            IPooledPage? page = null;

            try
            {
                // Act
                page = await browserPool.GetPageAsync();

                // Assert
                page.ShouldNotBeNull();
                page.ShouldBeAssignableTo<IPooledPage>();
                page.IsInUse.ShouldBeTrue();
            }
            finally
            {
                await DisposePoolAsync(browserPool, page);
            }
        }

        [Fact]
        public async Task GetPageAsync_MultipleRequests_ShouldReturnDifferentPages()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            IPooledPage? page1 = null;
            IPooledPage? page2 = null;

            try
            {
                // Act
                page1 = await browserPool.GetPageAsync();
                page2 = await browserPool.GetPageAsync();

                // Assert
                page1.ShouldNotBeNull();
                page2.ShouldNotBeNull();
                page1.ShouldNotBeSameAs(page2);
            }
            finally
            {
                await DisposePoolAsync(browserPool, page1, page2);
            }
        }

        [Fact]
        public async Task GetPageAsync_WhenDisposed_ShouldThrowException()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            browserPool.Dispose();

            // Act & Assert
            await Shouldly.Should.ThrowAsync<ObjectDisposedException>(async () => await browserPool.GetPageAsync());
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);

            // Act
            browserPool.Dispose();

            // Assert
            Shouldly.Should.NotThrow(() => browserPool.Dispose()); // Should handle multiple dispose calls
        }

        [Fact]
        public async Task ReturnPageAsync_ShouldMarkPageAsAvailable()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            IPooledPage? page = null;

            try
            {
                page = await browserPool.GetPageAsync();
                page.IsInUse.ShouldBeTrue();

                // Act
                await browserPool.ReturnPageAsync(page);

                // Assert
                page.IsInUse.ShouldBeFalse();
            }
            finally
            {
                await DisposePoolAsync(browserPool, page);
            }
        }

        [Fact]
        public async Task PooledPage_Dispose_ShouldReturnToPool()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            IPooledPage? page = null;

            try
            {
                page = await browserPool.GetPageAsync();

                // Act
                page.Dispose();

                // Assert
                // El dispose debería devolver la página al pool
                page.IsInUse.ShouldBeFalse();
            }
            finally
            {
                await DisposePoolAsync(browserPool, page);
            }
        }

        [Fact]
        public async Task GetPageAsync_WhenPoolExhausted_ShouldTimeout()
        {
            // Arrange
            var options = new BrowserPoolOptions
            {
                MaxInstances = 1,
                AcquireTimeoutSeconds = 1
            };
            var pool = new BrowserPool(options, _loggerMock.Object);

            try
            {
                // Ocupar la única página disponible
                await pool.GetPageAsync();

                // Act & Assert
                var exception = await Shouldly.Should.ThrowAsync<Exception>(async () => await pool.GetPageAsync());
                
                // Should be either timeout or browser creation error
                var message = exception.Message;
                (message.Contains("Timeout") || message.Contains("Error getting browser page")).ShouldBeTrue();

                // Skip cleanup to avoid semaphore issues - the test's purpose is to verify exception throwing
                // The pool disposal will handle cleanup
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Fact]
        public async Task ReturnPageAsync_WhenReturnedPageIsClosed_ShouldCloseItAndCreateReplacementOnNextAcquire()
        {
            var browserMock = CreateBrowserMock();
            var firstPageClosed = false;
            var firstPage = CreatePageMock(browserMock.Object, "about:blank");
            var secondPage = CreatePageMock(browserMock.Object, "about:blank");
            firstPage.SetupGet(x => x.IsClosed).Returns(() => firstPageClosed);
            var pageQueue = new Queue<IPage>(new[] { firstPage.Object, secondPage.Object });
            var createdPages = 0;

            await using var pool = new BrowserPool(
                new BrowserPoolOptions { MaxInstances = 1, AcquireTimeoutSeconds = 1 },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult(browserMock.Object),
                    CreatePageAsync = _ =>
                    {
                        createdPages++;
                        return Task.FromResult(pageQueue.Dequeue());
                    },
                    ClosePageAsync = _ => Task.CompletedTask,
                    CloseBrowserAsync = _ => Task.CompletedTask
                });

            var originalPage = await pool.GetPageAsync();
            firstPageClosed = true;
            await pool.ReturnPageAsync(originalPage);

            var replacementPage = await pool.GetPageAsync();

            createdPages.ShouldBe(2);
            replacementPage.ShouldNotBeSameAs(originalPage);
            replacementPage.Page.ShouldBeSameAs(secondPage.Object);
        }

        [Fact]
        public async Task GetPageAsync_WhenPageCreationFailsInitially_ShouldRetryWithFreshBrowser()
        {
            var firstBrowser = CreateBrowserMock();
            var secondBrowser = CreateBrowserMock();
            var launchedBrowsers = 0;
            var createAttempts = 0;
            var replacementPage = CreatePageMock(secondBrowser.Object, "about:blank");

            await using var pool = new BrowserPool(
                new BrowserPoolOptions { MaxInstances = 1, AcquireTimeoutSeconds = 1 },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult<IBrowser>(launchedBrowsers++ == 0 ? firstBrowser.Object : secondBrowser.Object),
                    CreatePageAsync = _ =>
                    {
                        createAttempts++;
                        if (createAttempts == 1)
                        {
                            throw new InvalidOperationException("page creation failed");
                        }

                        return Task.FromResult(replacementPage.Object);
                    },
                    ClosePageAsync = _ => Task.CompletedTask,
                    CloseBrowserAsync = _ => Task.CompletedTask
                });

            var page = await pool.GetPageAsync();

            page.ShouldNotBeNull();
            page.Page.ShouldBe(replacementPage.Object);
            createAttempts.ShouldBe(2);
            launchedBrowsers.ShouldBe(2);
            firstBrowser.Verify(x => x.CloseAsync(), Times.Once);
        }

        private static Mock<IBrowser> CreateBrowserMock()
        {
            var browserMock = new Mock<IBrowser>();
            browserMock.SetupGet(x => x.IsClosed).Returns(false);
            browserMock.Setup(x => x.CloseAsync()).Returns(Task.CompletedTask);
            return browserMock;
        }

        private static Mock<IPage> CreatePageMock(IBrowser browser, string url)
        {
            var pageMock = new Mock<IPage>();
            pageMock.SetupGet(x => x.IsClosed).Returns(false);
            pageMock.SetupGet(x => x.Browser).Returns(browser);
            pageMock.SetupGet(x => x.Url).Returns(url);
            return pageMock;
        }

        private sealed class EnvironmentVariableScope : IDisposable
        {
            private readonly string _name;
            private readonly string? _originalValue;

            public EnvironmentVariableScope(string name, string? value)
            {
                _name = name;
                _originalValue = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(_name, _originalValue);
            }
        }
    }
}
