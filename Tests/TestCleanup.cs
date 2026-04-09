using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Abstractions;
using Xunit;

namespace Nast.Html2Pdf.Tests
{
    /// <summary>
    /// Shared test cleanup avoids global process killing and relies on owned-resource disposal.
    /// </summary>
    public sealed class TestCleanup : IAsyncLifetime
    {
        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }

    internal static class TestOwnedResourceCleanup
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        public static async Task DisposeServiceProviderOwnedResourcesAsync(IServiceProvider? serviceProvider, Action<string>? log = null)
        {
            if (serviceProvider == null)
            {
                return;
            }

            if (serviceProvider.GetService(typeof(IBrowserPool)) is IAsyncDisposable browserPool)
            {
                await DisposeOwnedAsync(browserPool, DefaultTimeout, "browser pool", log);
            }

            if (serviceProvider is IAsyncDisposable asyncDisposable)
            {
                await DisposeOwnedAsync(asyncDisposable, DefaultTimeout, "service provider", log);
                return;
            }

            if (serviceProvider is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Error disposing service provider: {ex.Message}");
                }
            }
        }

        private static async Task DisposeOwnedAsync(IAsyncDisposable resource, TimeSpan timeout, string resourceName, Action<string>? log)
        {
            try
            {
                await resource.DisposeAsync().AsTask().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                log?.Invoke($"Timed out disposing {resourceName} after {timeout.TotalSeconds:0} seconds.");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error disposing {resourceName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Collection fixture to ensure cleanup runs once per test assembly
    /// </summary>
    [CollectionDefinition("TestCleanup")]
    public class TestCleanupCollectionDefinition : ICollectionFixture<TestCleanup>
    {
    }
}
