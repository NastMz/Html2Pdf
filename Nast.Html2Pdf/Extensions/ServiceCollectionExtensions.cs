using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nast.Html2Pdf.Interfaces;
using Nast.Html2Pdf.Models;
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

            // Registrar servicios
            services.AddSingleton<IBrowserPool>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<BrowserPoolOptions>>().Value;
                var logger = serviceProvider.GetRequiredService<ILogger<BrowserPool>>();
                return new BrowserPool(options, logger);
            });

            services.AddScoped<IHtmlGenerator, HtmlGenerator>();
            services.AddScoped<IPdfConverter, PdfConverter>();
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

            // Registrar servicios con el lifetime especificado
            services.Add(new ServiceDescriptor(typeof(IHtmlGenerator), typeof(HtmlGenerator), serviceLifetime));
            services.Add(new ServiceDescriptor(typeof(IPdfConverter), typeof(PdfConverter), serviceLifetime));
            services.Add(new ServiceDescriptor(typeof(IHtml2PdfService), typeof(Html2PdfService), serviceLifetime));

            return services;
        }
    }

    /// <summary>
    /// Extensiones para facilitar el uso del servicio
    /// </summary>
    public static class Html2PdfServiceExtensions
    {
        /// <summary>
        /// Genera un PDF y lo guarda en un archivo
        /// </summary>
        public static async Task<bool> GeneratePdfToFileAsync(this IHtml2PdfService service,
            string template, object? model, string outputPath, 
            PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null)
        {
            var result = await service.GeneratePdfAsync(template, model, pdfOptions, htmlOptions);
            
            if (result.Success && result.Data != null)
            {
                await File.WriteAllBytesAsync(outputPath, result.Data);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Genera un PDF desde un archivo de plantilla y lo guarda en un archivo
        /// </summary>
        public static async Task<bool> GeneratePdfFromFileToFileAsync(this IHtml2PdfService service,
            string templatePath, object? model, string outputPath, 
            PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null)
        {
            var result = await service.GeneratePdfFromFileAsync(templatePath, model, pdfOptions, htmlOptions);
            
            if (result.Success && result.Data != null)
            {
                await File.WriteAllBytesAsync(outputPath, result.Data);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Genera un PDF desde HTML y lo guarda en un archivo
        /// </summary>
        public static async Task<bool> GeneratePdfFromHtmlToFileAsync(this IHtml2PdfService service,
            string html, string outputPath, PdfOptions? pdfOptions = null)
        {
            var result = await service.GeneratePdfFromHtmlAsync(html, pdfOptions);
            
            if (result.Success && result.Data != null)
            {
                await File.WriteAllBytesAsync(outputPath, result.Data);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Genera un PDF desde una URL y lo guarda en un archivo
        /// </summary>
        public static async Task<bool> GeneratePdfFromUrlToFileAsync(this IHtml2PdfService service,
            string url, string outputPath, PdfOptions? pdfOptions = null)
        {
            var result = await service.GeneratePdfFromUrlAsync(url, pdfOptions);
            
            if (result.Success && result.Data != null)
            {
                await File.WriteAllBytesAsync(outputPath, result.Data);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Genera un PDF y lo retorna como un Stream
        /// </summary>
        public static async Task<Stream?> GeneratePdfToStreamAsync(this IHtml2PdfService service,
            string template, object? model, 
            PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null)
        {
            var result = await service.GeneratePdfAsync(template, model, pdfOptions, htmlOptions);
            
            if (result.Success && result.Data != null)
            {
                return new MemoryStream(result.Data);
            }
            
            return null;
        }
    }
}
