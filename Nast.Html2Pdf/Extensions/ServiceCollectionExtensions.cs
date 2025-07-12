using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Services;

namespace Nast.Html2Pdf.Extensions
{
    /// <summary>
    /// Extensions for service configuration
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all Html2Pdf services in the dependency container
        /// </summary>
        public static IServiceCollection AddHtml2Pdf(this IServiceCollection services)
        {
            return services.AddHtml2Pdf(options => { });
        }

        /// <summary>
        /// Registers all Html2Pdf services in the dependency container with options
        /// </summary>
        public static IServiceCollection AddHtml2Pdf(this IServiceCollection services,
            Action<BrowserPoolOptions> configureBrowserPool)
        {
            // Configure browser pool options
            services.Configure(configureBrowserPool);

            // Register browser pool as a singleton
            services.AddSingleton<IBrowserPool>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<BrowserPoolOptions>>().Value;
                var logger = serviceProvider.GetRequiredService<ILogger<BrowserPool>>();
                return new BrowserPool(options, logger);
            });

            services.AddScoped<IHtmlGenerator, HtmlGenerator>();
            services.AddScoped<IPdfConverter, PdfConverter>();
            services.AddScoped<Html2PdfDiagnostics>();
            services.AddScoped<IHtml2PdfService, Html2PdfService>();

            return services;
        }

        /// <summary>
        /// Registers all Html2Pdf services in the dependency container with advanced configuration
        /// </summary>
        public static IServiceCollection AddHtml2PdfWithLifetime(this IServiceCollection services,
            Action<BrowserPoolOptions> configureBrowserPool,
            ServiceLifetime browserPoolLifetime = ServiceLifetime.Singleton,
            ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
        {
            // Configure browser pool options
            services.Configure(configureBrowserPool);

            // Register browser pool with specified lifetime
            services.Add(new ServiceDescriptor(typeof(IBrowserPool), serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<BrowserPoolOptions>>().Value;
                var logger = serviceProvider.GetRequiredService<ILogger<BrowserPool>>();
                return new BrowserPool(options, logger);
            }, browserPoolLifetime));

            // Register other services with specified lifetime
            services.Add(new ServiceDescriptor(typeof(IHtmlGenerator), typeof(HtmlGenerator), serviceLifetime));
            services.Add(new ServiceDescriptor(typeof(IPdfConverter), typeof(PdfConverter), serviceLifetime));
            services.Add(new ServiceDescriptor(typeof(Html2PdfDiagnostics), typeof(Html2PdfDiagnostics), serviceLifetime));
            services.Add(new ServiceDescriptor(typeof(IHtml2PdfService), typeof(Html2PdfService), serviceLifetime));

            return services;
        }
    }
}
