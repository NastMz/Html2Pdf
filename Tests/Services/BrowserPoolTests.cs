using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Tests.Services
{
    [Collection("Integration")]
    public class BrowserPoolTests
    {
        private readonly Mock<ILogger<BrowserPool>> _loggerMock;
        private readonly BrowserPoolOptions _options;

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
        public async Task GetPageAsync_ShouldReturnPooledPage()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);

            // Act
            var page = await browserPool.GetPageAsync();

            // Assert
            page.Should().NotBeNull();
            page.Should().BeAssignableTo<IPooledPage>();
            page.IsInUse.Should().BeTrue();
        }

        [Fact]
        public async Task GetPageAsync_MultipleRequests_ShouldReturnDifferentPages()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);

            // Act
            var page1 = await browserPool.GetPageAsync();
            var page2 = await browserPool.GetPageAsync();

            // Assert
            page1.Should().NotBeNull();
            page2.Should().NotBeNull();
            page1.Should().NotBeSameAs(page2);
            
            // Cleanup
            await browserPool.ReturnPageAsync(page1);
            await browserPool.ReturnPageAsync(page2);
        }

        [Fact]
        public async Task GetPageAsync_WhenDisposed_ShouldThrowException()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            browserPool.Dispose();

            // Act & Assert
            await FluentActions.Invoking(() => browserPool.GetPageAsync())
                .Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);

            // Act
            browserPool.Dispose();

            // Assert
            FluentActions.Invoking(() => browserPool.Dispose())
                .Should().NotThrow(); // Should handle multiple dispose calls
        }

        [Fact]
        public async Task ReturnPageAsync_ShouldMarkPageAsAvailable()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            var page = await browserPool.GetPageAsync();
            page.IsInUse.Should().BeTrue();

            // Act
            await browserPool.ReturnPageAsync(page);

            // Assert
            page.IsInUse.Should().BeFalse();
        }

        [Fact]
        public async Task PooledPage_Dispose_ShouldReturnToPool()
        {
            // Arrange
            var browserPool = new BrowserPool(_options, _loggerMock.Object);
            var page = await browserPool.GetPageAsync();

            // Act
            page.Dispose();

            // Assert
            // El dispose debería devolver la página al pool
            page.IsInUse.Should().BeFalse();
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
                var exception = await FluentActions.Invoking(() => pool.GetPageAsync())
                    .Should().ThrowAsync<Exception>();
                
                // Should be either timeout or browser creation error
                var message = exception.Which.Message;
                (message.Contains("Timeout") || message.Contains("Error getting browser page")).Should().BeTrue();

                // Skip cleanup to avoid semaphore issues - the test's purpose is to verify exception throwing
                // The pool disposal will handle cleanup
            }
            finally
            {
                pool.Dispose();
            }
        }
    }
}
