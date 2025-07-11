using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Extensions;
using Nast.Html2Pdf.Services;

namespace Nast.Html2Pdf
{
    /// <summary>
    /// Main factory class for HTML to PDF conversion
    /// </summary>
    public static class Html2PdfFactory
    {
        /// <summary>
        /// Creates a new instance of the Html2Pdf service without dependency injection
        /// </summary>
        public static IHtml2PdfService Create(BrowserPoolOptions? browserPoolOptions = null)
        {
            var services = new ServiceCollection();

            // Configure basic logging
            services.AddLogging(builder => builder.AddConsole());

            // Add Html2Pdf services
            services.AddHtml2Pdf(options =>
            {
                if (browserPoolOptions != null)
                {
                    options.MinInstances = browserPoolOptions.MinInstances;
                    options.MaxInstances = browserPoolOptions.MaxInstances;
                    options.MaxLifetimeMinutes = browserPoolOptions.MaxLifetimeMinutes;
                    options.AcquireTimeoutSeconds = browserPoolOptions.AcquireTimeoutSeconds;
                    options.AdditionalArgs = browserPoolOptions.AdditionalArgs;
                    options.Headless = browserPoolOptions.Headless;
                }
            });

            var serviceProvider = services.BuildServiceProvider();
            return serviceProvider.GetRequiredService<IHtml2PdfService>();
        }

        /// <summary>
        /// Creates a new instance of the Html2Pdf service with default configurations
        /// </summary>
        public static IHtml2PdfService CreateDefault()
        {
            return Create(new BrowserPoolOptions
            {
                MinInstances = 1,
                MaxInstances = 3,
                MaxLifetimeMinutes = 30,
                AcquireTimeoutSeconds = 30,
                Headless = true
            });
        }
    }
}
