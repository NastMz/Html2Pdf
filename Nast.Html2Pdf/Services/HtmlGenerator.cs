using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Exceptions;
using Nast.Html2Pdf.Models;
using RazorLight;
using System.Diagnostics;
using System.Text;

namespace Nast.Html2Pdf.Services
{
    /// <summary>
    /// HTML generator using RazorLight for template processing
    /// </summary>
    internal sealed class HtmlGenerator : IHtmlGenerator
    {
        private readonly IRazorLightEngine _razorEngine;
        private readonly ILogger<HtmlGenerator> _logger;

        public HtmlGenerator(ILogger<HtmlGenerator> logger)
        {
            _logger = logger;
            _razorEngine = new RazorLightEngineBuilder()
                .UseEmbeddedResourcesProject(typeof(HtmlGenerator).Assembly)
                .UseMemoryCachingProvider()
                .Build();
        }

        public async Task<HtmlResult> GenerateAsync(string template, object? model = null, HtmlGenerationOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            options ??= new HtmlGenerationOptions();

            try
            {
                _logger.LogDebug("Starting HTML generation from template");

                // Generate base HTML
                var html = await _razorEngine.CompileRenderStringAsync(Guid.NewGuid().ToString(), template, model);

                // Process HTML according to options
                var processedHtml = ProcessHtml(html, options);

                stopwatch.Stop();
                _logger.LogDebug("HTML generation completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return HtmlResult.CreateSuccess(processedHtml, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating HTML from template");
                return HtmlResult.CreateError($"Error generating HTML: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        public async Task<HtmlResult> GenerateFromFileAsync(string templatePath, object? model = null, HtmlGenerationOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            options ??= new HtmlGenerationOptions();

            try
            {
                _logger.LogDebug("Starting HTML generation from file: {TemplatePath}", templatePath);

                if (!File.Exists(templatePath))
                {
                    throw new TemplateException($"Template file not found: {templatePath}");
                }

                // Set base path if not specified
                if (string.IsNullOrEmpty(options.BasePath))
                {
                    options.BasePath = Path.GetDirectoryName(templatePath);
                }

                var html = await _razorEngine.CompileRenderAsync(templatePath, model);

                // Process HTML according to options
                var processedHtml = ProcessHtml(html, options);

                stopwatch.Stop();
                _logger.LogDebug("HTML generation completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return HtmlResult.CreateSuccess(processedHtml, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating HTML from file: {TemplatePath}", templatePath);
                return HtmlResult.CreateError($"Error generating HTML from file: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        public async Task<HtmlResult> GenerateFromResourceAsync(string resourceKey, object? model = null, HtmlGenerationOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            options ??= new HtmlGenerationOptions();

            try
            {
                _logger.LogDebug("Starting HTML generation from resource: {ResourceKey}", resourceKey);

                var html = await _razorEngine.CompileRenderAsync(resourceKey, model);

                // Procesar el HTML según las opciones
                var processedHtml = ProcessHtml(html, options);

                stopwatch.Stop();
                _logger.LogDebug("HTML generation completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return HtmlResult.CreateSuccess(processedHtml, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating HTML from resource: {ResourceKey}", resourceKey);
                return HtmlResult.CreateError($"Error generating HTML from resource: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        private static string ProcessHtml(string html, HtmlGenerationOptions options)
        {
            // Check if it's already a complete HTML document
            if (IsCompleteHtmlDocument(html))
            {
                return ProcessCompleteHtmlDocument(html, options);
            }
            else
            {
                return CreateCompleteHtmlDocument(html, options);
            }
        }

        private static bool IsCompleteHtmlDocument(string html)
        {
            var trimmedHtml = html.TrimStart();
            return trimmedHtml.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                   trimmedHtml.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateCompleteHtmlDocument(string html, HtmlGenerationOptions options)
        {
            var htmlBuilder = new StringBuilder();

            htmlBuilder.AppendLine("<!DOCTYPE html>");
            htmlBuilder.AppendLine("<html>");
            htmlBuilder.AppendLine("<head>");
            htmlBuilder.AppendLine($"<meta charset=\"{options.Encoding}\">");

            if (options.IncludeViewport)
            {
                htmlBuilder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            }

            // Agregar estilos CSS adicionales
            if (!string.IsNullOrEmpty(options.AdditionalCss))
            {
                htmlBuilder.AppendLine("<style>");
                htmlBuilder.AppendLine(options.AdditionalCss);
                htmlBuilder.AppendLine("</style>");
            }

            htmlBuilder.AppendLine("</head>");
            htmlBuilder.AppendLine("<body>");
            htmlBuilder.AppendLine(html);
            htmlBuilder.AppendLine("</body>");

            // Agregar scripts JavaScript adicionales
            if (!string.IsNullOrEmpty(options.AdditionalJs))
            {
                htmlBuilder.AppendLine("<script>");
                htmlBuilder.AppendLine(options.AdditionalJs);
                htmlBuilder.AppendLine("</script>");
            }

            htmlBuilder.AppendLine("</html>");

            return htmlBuilder.ToString();
        }

        private static string ProcessCompleteHtmlDocument(string html, HtmlGenerationOptions options)
        {
            if (string.IsNullOrEmpty(options.AdditionalCss) && string.IsNullOrEmpty(options.AdditionalJs))
            {
                return html;
            }

            var htmlContent = html;

            // Agregar CSS adicional antes del cierre de </head>
            if (!string.IsNullOrEmpty(options.AdditionalCss))
            {
                htmlContent = InsertBeforeTag(htmlContent, "</head>", $"<style>{options.AdditionalCss}</style>\n");
            }

            // Agregar JS adicional antes del cierre de </body>
            if (!string.IsNullOrEmpty(options.AdditionalJs))
            {
                htmlContent = InsertBeforeTag(htmlContent, "</body>", $"<script>{options.AdditionalJs}</script>\n");
            }

            return htmlContent;
        }

        private static string InsertBeforeTag(string html, string tag, string content)
        {
            var tagIndex = html.LastIndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (tagIndex > -1)
            {
                return html.Insert(tagIndex, content);
            }
            return html;
        }
    }
}
