using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Abstractions;

namespace Nast.Html2Pdf.Tests
{
    public class TestOwnedResourceCleanupTests
    {
        [Fact]
        public async Task DisposeServiceProviderOwnedResourcesAsync_ShouldDisposeBrowserPoolBeforeServiceProvider()
        {
            var events = new List<string>();
            var browserPool = new FakeBrowserPool(() => events.Add("pool"));
            await using var serviceProvider = new FakeServiceProvider(browserPool, () => events.Add("provider"));

            await TestOwnedResourceCleanup.DisposeServiceProviderOwnedResourcesAsync(serviceProvider);

            events.ShouldBe(["pool", "provider"]);
        }

        [Fact]
        public async Task DisposeServiceProviderOwnedResourcesAsync_ShouldDisposeProviderWhenBrowserPoolIsMissing()
        {
            var events = new List<string>();
            await using var serviceProvider = new FakeServiceProvider(null, () => events.Add("provider"));

            await TestOwnedResourceCleanup.DisposeServiceProviderOwnedResourcesAsync(serviceProvider);

            events.ShouldBe(["provider"]);
        }

        private sealed class FakeServiceProvider(IBrowserPool? browserPool, Action onDispose) : IServiceProvider, IAsyncDisposable
        {
            public object? GetService(Type serviceType)
            {
                return serviceType == typeof(IBrowserPool) ? browserPool : null;
            }

            public ValueTask DisposeAsync()
            {
                onDispose();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class FakeBrowserPool(Action onDispose) : IBrowserPool
        {
            public Task<IPooledPage> GetPageAsync() => throw new NotSupportedException();

            public Task ReturnPageAsync(IPooledPage page) => throw new NotSupportedException();

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync()
            {
                onDispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
