using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace Nast.Html2Pdf.Tests.Services
{
    public class BrowserPoolInstrumentationTests
    {
        private readonly Mock<ILogger<BrowserPool>> _loggerMock = new();

        [Fact]
        public async Task GetPageAsync_ShouldRecordLifecycleProvisioningAndBrowserInteropSeparately()
        {
            var measurements = new List<BrowserPoolMeasurement>();
            var browserMock = CreateBrowserMock();
            var pageMock = CreatePageMock(browserMock.Object, "about:blank");

            await using var pool = new BrowserPool(
                new BrowserPoolOptions
                {
                    MaxInstances = 1,
                    AcquireTimeoutSeconds = 1
                },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    OnMeasurement = measurements.Add,
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult(browserMock.Object),
                    CreatePageAsync = _ => Task.FromResult(pageMock.Object),
                    ClosePageAsync = _ => Task.CompletedTask,
                    CloseBrowserAsync = _ => Task.CompletedTask
                });

            var pooledPage = await pool.GetPageAsync();
            await pool.ReturnPageAsync(pooledPage);

            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.AcquirePage && x.Category == BrowserPoolMeasurementCategory.PoolLifecycle && x.Succeeded);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.BrowserProvisioning && x.Category == BrowserPoolMeasurementCategory.Provisioning && x.Succeeded);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.BrowserStartup && x.Category == BrowserPoolMeasurementCategory.BrowserInterop && x.Succeeded);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.PageCreation && x.Category == BrowserPoolMeasurementCategory.BrowserInterop && x.Succeeded);
        }

        [Fact]
        public async Task ReturnAndDispose_ShouldRecordBookkeepingAndBrowserCleanupSeparately()
        {
            var measurements = new List<BrowserPoolMeasurement>();
            var browserMock = CreateBrowserMock();
            var pageMock = CreatePageMock(browserMock.Object, "about:blank");
            var closePageCalls = 0;
            var closeBrowserCalls = 0;

            await using var pool = new BrowserPool(
                new BrowserPoolOptions
                {
                    MaxInstances = 1,
                    AcquireTimeoutSeconds = 1
                },
                _loggerMock.Object,
                new BrowserPoolRuntimeHooks
                {
                    OnMeasurement = measurements.Add,
                    EnsureBrowserProvisionedAsync = _ => Task.CompletedTask,
                    LaunchBrowserAsync = (_, _) => Task.FromResult(browserMock.Object),
                    CreatePageAsync = _ => Task.FromResult(pageMock.Object),
                    ClosePageAsync = _ =>
                    {
                        closePageCalls++;
                        return Task.CompletedTask;
                    },
                    CloseBrowserAsync = _ =>
                    {
                        closeBrowserCalls++;
                        return Task.CompletedTask;
                    }
                });

            var pooledPage = await pool.GetPageAsync();

            await pool.ReturnPageAsync(pooledPage);
            await pool.DisposeAsync();

            closePageCalls.ShouldBe(1);
            closeBrowserCalls.ShouldBe(1);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.ReturnPage && x.Category == BrowserPoolMeasurementCategory.PoolLifecycle && x.Succeeded);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.DisposePool && x.Category == BrowserPoolMeasurementCategory.PoolLifecycle && x.Succeeded);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.ClosePage && x.Category == BrowserPoolMeasurementCategory.BrowserInterop && x.Succeeded);
            measurements.ShouldContain(x => x.Operation == BrowserPoolOperation.CloseBrowser && x.Category == BrowserPoolMeasurementCategory.BrowserInterop && x.Succeeded);
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
    }
}
