using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Models;
using System.Diagnostics;
using PuppeteerPdfOptions = PuppeteerSharp.PdfOptions;
using ModelPdfOptions = Nast.Html2Pdf.Models.PdfOptions;

namespace Nast.Html2Pdf.Services
{
    /// <summary>
    /// HTML to PDF converter using Playwright
    /// </summary>
    internal sealed class PdfConverter : IPdfConverter
    {
        private readonly IBrowserPool _browserPool;
        private readonly ILogger<PdfConverter> _logger;

        public PdfConverter(IBrowserPool browserPool, ILogger<PdfConverter> logger)
        {
            _browserPool = browserPool;
            _logger = logger;
        }

        public async Task<PdfResult> ConvertAsync(string html, ModelPdfOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            options ??= new ModelPdfOptions();

            try
            {
                _logger.LogDebug("Starting PDF conversion from HTML");

                using var page = await _browserPool.GetPageAsync();

                // Configure the page
                await ConfigurePageAsync(page.Page, options);

                // Load HTML
                await page.Page.SetContentAsync(html, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                    Timeout = options.TimeoutMs
                });

                // Esperar a que se carguen las imágenes si está configurado
                if (options.WaitForImages)
                {
                    await WaitForImagesAsync(page.Page);
                }

                // Generar PDF
                var pdfData = await GeneratePdfAsync(page.Page, options);

                stopwatch.Stop();
                _logger.LogDebug("PDF conversion completed in {Duration}ms, size: {Size} bytes",
                    stopwatch.ElapsedMilliseconds, pdfData.Length);

                return PdfResult.CreateSuccess(pdfData, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error converting HTML to PDF");
                return PdfResult.CreateError($"Error converting HTML to PDF: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        public async Task<PdfResult> ConvertFromUrlAsync(string url, ModelPdfOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            options ??= new ModelPdfOptions();

            try
            {
                _logger.LogDebug("Starting PDF conversion from URL: {Url}", url);

                using var page = await _browserPool.GetPageAsync();

                // Configure the page
                await ConfigurePageAsync(page.Page, options);

                // Navigate to URL
                await page.Page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                    Timeout = options.TimeoutMs
                });

                // Esperar a que se carguen las imágenes si está configurado
                if (options.WaitForImages)
                {
                    await WaitForImagesAsync(page.Page);
                }

                // Generar PDF
                var pdfData = await GeneratePdfAsync(page.Page, options);

                stopwatch.Stop();
                _logger.LogDebug("PDF conversion from URL completed in {Duration}ms, size: {Size} bytes",
                    stopwatch.ElapsedMilliseconds, pdfData.Length);

                return PdfResult.CreateSuccess(pdfData, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error converting URL to PDF: {Url}", url);
                return PdfResult.CreateError($"Error converting URL to PDF: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        private static async Task ConfigurePageAsync(IPage page, ModelPdfOptions options)
        {
            // Configure color scheme
            await page.EmulateMediaTypeAsync(MediaType.Print);

            // Configure viewport if necessary
            if (options.Width.HasValue && options.Height.HasValue)
            {
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = (int)(options.Width.Value * 96),
                    Height = (int)(options.Height.Value * 96)
                });
            }
        }

        private async Task<byte[]> GeneratePdfAsync(IPage page, ModelPdfOptions options)
        {
            var pdfOptions = new PuppeteerPdfOptions
            {
                Format = GetPaperFormat(options.Format),
                Landscape = options.Landscape,
                PrintBackground = options.PrintBackground,
                Scale = (decimal)options.Scale,
                PreferCSSPageSize = true
            };

            // Configure margins
            if (options.Margins != null)
            {
                pdfOptions.MarginOptions = new MarginOptions
                {
                    Top = options.Margins.Top,
                    Bottom = options.Margins.Bottom,
                    Left = options.Margins.Left,
                    Right = options.Margins.Right
                };
            }

            // Configure custom dimensions
            if (options.Width.HasValue)
            {
                pdfOptions.Width = $"{options.Width.Value}in";
            }

            if (options.Height.HasValue)
            {
                pdfOptions.Height = $"{options.Height.Value}in";
            }

            // Configure page range
            if (!string.IsNullOrEmpty(options.PageRanges))
            {
                pdfOptions.PageRanges = options.PageRanges;
            }

            // Configure header
            if (options.Header != null)
            {
                pdfOptions.HeaderTemplate = await ProcessHeaderFooterTemplate(options.Header);
                pdfOptions.DisplayHeaderFooter = true;
            }

            // Configure footer
            if (options.Footer != null)
            {
                pdfOptions.FooterTemplate = await ProcessHeaderFooterTemplate(options.Footer);
                pdfOptions.DisplayHeaderFooter = true;
            }

            return await page.PdfDataAsync(pdfOptions);
        }

        private async Task<string> ProcessHeaderFooterTemplate(PdfHeaderFooter headerFooter)
        {
            try
            {
                if (headerFooter.Data == null)
                {
                    return headerFooter.Template;
                }

                // Si hay datos, usar RazorLight para procesar la plantilla
                var htmlGenerator = new HtmlGenerator(new Microsoft.Extensions.Logging.Abstractions.NullLogger<HtmlGenerator>());
                var result = await htmlGenerator.GenerateAsync(headerFooter.Template, headerFooter.Data);

                if (result.Success)
                {
                    return result.Html ?? headerFooter.Template;
                }
                else
                {
                    _logger.LogWarning("Error processing header/footer template: {Error}", result.ErrorMessage);
                    return headerFooter.Template;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing header/footer template");
                return headerFooter.Template;
            }
        }

        private async Task WaitForImagesAsync(IPage page)
        {
            try
            {
                // Esperar a que todas las imágenes se carguen
                await page.WaitForFunctionAsync(@"
                    () => {
                        const images = Array.from(document.images);
                        return images.every(img => img.complete);
                    }
                ", new WaitForFunctionOptions { Timeout = 5000 });
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timeout waiting for images to load");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for images to load");
            }
        }

        private static PaperFormat GetPaperFormat(string format)
        {
            return format.ToUpperInvariant() switch
            {
                "A4" => PaperFormat.A4,
                "A3" => PaperFormat.A3,
                "A5" => PaperFormat.A5,
                "LETTER" => PaperFormat.Letter,
                "LEGAL" => PaperFormat.Legal,
                "TABLOID" => PaperFormat.Tabloid,
                "LEDGER" => PaperFormat.Ledger,
                _ => PaperFormat.A4
            };
        }
    }
}
