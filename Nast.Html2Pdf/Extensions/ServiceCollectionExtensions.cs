using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Helpers;
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
            var validatedOptions = CreateValidatedBrowserPoolOptions(configureBrowserPool);

            // Configure browser pool options
            services.Configure<BrowserPoolOptions>(options => CopyOptions(validatedOptions, options));

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
            ValidateServiceLifetime(browserPoolLifetime, nameof(browserPoolLifetime));
            ValidateServiceLifetime(serviceLifetime, nameof(serviceLifetime));

            var validatedOptions = CreateValidatedBrowserPoolOptions(configureBrowserPool);

            // Configure browser pool options
            services.Configure<BrowserPoolOptions>(options => CopyOptions(validatedOptions, options));

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

        private static BrowserPoolOptions CreateValidatedBrowserPoolOptions(Action<BrowserPoolOptions> configureBrowserPool)
        {
            ArgumentNullException.ThrowIfNull(configureBrowserPool);

            var options = new BrowserPoolOptions();
            configureBrowserPool(options);
            return Html2PdfValidation.ValidateBrowserPoolOptions(options);
        }

        private static void CopyOptions(BrowserPoolOptions source, BrowserPoolOptions destination)
        {
            destination.MinInstances = source.MinInstances;
            destination.MaxInstances = source.MaxInstances;
            destination.MaxLifetimeMinutes = source.MaxLifetimeMinutes;
            destination.AcquireTimeoutSeconds = source.AcquireTimeoutSeconds;
            destination.AdditionalArgs = source.AdditionalArgs;
            destination.Headless = source.Headless;
            destination.ExecutablePath = source.ExecutablePath;
            destination.BrowserCacheDirectory = source.BrowserCacheDirectory;
            destination.SkipBrowserDownload = source.SkipBrowserDownload;
            destination.AllowInsecureCertificates = source.AllowInsecureCertificates;
            destination.DisableSandbox = source.DisableSandbox;
        }

        private static void ValidateServiceLifetime(ServiceLifetime lifetime, string parameterName)
        {
            if (!Enum.IsDefined(lifetime))
            {
                throw new ArgumentOutOfRangeException(parameterName, lifetime, $"{parameterName} must be a valid ServiceLifetime value.");
            }
        }
    }
}
