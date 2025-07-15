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

            const int maxRetries = 2;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Starting PDF conversion from HTML (attempt {Attempt})", attempt);

                    using var page = await _browserPool.GetPageAsync();

                    // Check if page is null
                    if (page?.Page == null)
                    {
                        throw new InvalidOperationException("Failed to get a valid page from browser pool");
                    }

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
                catch (Exception ex) when (IsRetryableException(ex) && attempt < maxRetries)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Retryable error on attempt {Attempt}/{MaxRetries}: {Message}", 
                        attempt, maxRetries, ex.Message);
                    await Task.Delay(500 * attempt); // Exponential backoff
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Error converting HTML to PDF");
                    return PdfResult.CreateError($"Error converting HTML to PDF: {ex.Message}", ex, stopwatch.Elapsed);
                }
            }

            stopwatch.Stop();
            _logger.LogError(lastException, "PDF conversion failed after {MaxRetries} attempts", maxRetries);
            return PdfResult.CreateError($"Error converting HTML to PDF after {maxRetries} attempts: {lastException?.Message}", 
                lastException, stopwatch.Elapsed);
        }

        private static bool IsRetryableException(Exception ex)
        {
            // Check for common PuppeteerSharp exceptions that indicate connection issues
            return ex.Message.Contains("WebSocket") || 
                   ex.Message.Contains("Target closed") || 
                   ex.Message.Contains("Navigating frame was detached") ||
                   ex.Message.Contains("Protocol error") ||
                   ex.Message.Contains("remote party closed") ||
                   ex.Message.Contains("Object reference not set to an instance of an object") ||
                   ex is NullReferenceException;
        }

        public async Task<PdfResult> ConvertFromUrlAsync(string url, ModelPdfOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            options ??= new ModelPdfOptions();

            const int maxRetries = 2;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Starting PDF conversion from URL: {Url} (attempt {Attempt})", url, attempt);

                    using var page = await _browserPool.GetPageAsync();

                    // Check if page is null
                    if (page?.Page == null)
                    {
                        throw new InvalidOperationException("Failed to get a valid page from browser pool");
                    }

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
                catch (Exception ex) when (IsRetryableException(ex) && attempt < maxRetries)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Retryable error on attempt {Attempt}/{MaxRetries}: {Message}", 
                        attempt, maxRetries, ex.Message);
                    await Task.Delay(500 * attempt); // Exponential backoff
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Error converting URL to PDF: {Url}", url);
                    return PdfResult.CreateError($"Error converting URL to PDF: {ex.Message}", ex, stopwatch.Elapsed);
                }
            }

            stopwatch.Stop();
            _logger.LogError(lastException, "PDF conversion from URL failed after {MaxRetries} attempts", maxRetries);
            return PdfResult.CreateError($"Error converting URL to PDF after {maxRetries} attempts: {lastException?.Message}", 
                lastException, stopwatch.Elapsed);
        }

        private static async Task ConfigurePageAsync(IPage page, ModelPdfOptions options)
        {
            try
            {
                // Configure color scheme
                await page.EmulateMediaTypeAsync(MediaType.Print);
            }
            catch (Exception ex) when (ex.Message.Contains("Session closed") || ex.Message.Contains("WebSocket"))
            {
                // Skip media type configuration if WebSocket connection is closed
                // This can happen in CI environments with limited resources
                System.Diagnostics.Debug.WriteLine($"Warning: Could not configure media type: {ex.Message}");
            }

            try
            {
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
            catch (Exception ex) when (ex.Message.Contains("Session closed") || ex.Message.Contains("WebSocket"))
            {
                // Skip viewport configuration if WebSocket connection is closed
                System.Diagnostics.Debug.WriteLine($"Warning: Could not configure viewport: {ex.Message}");
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
                _logger.LogDebug("Starting image loading wait");
                
                // Estrategia simple: esperar un tiempo fijo y verificar estado
                var maxAttempts = 10;
                var attemptCount = 0;
                var delayMs = 200;
                
                while (attemptCount < maxAttempts)
                {
                    try
                    {
                        // Verificar si hay imágenes
                        var imageCount = await page.EvaluateExpressionAsync<int>("document.images.length");
                        
                        if (imageCount == 0)
                        {
                            _logger.LogDebug("No images found, skipping wait");
                            return;
                        }
                        
                        // Contar imágenes cargadas
                        var loadedImages = await page.EvaluateExpressionAsync<int>(@"
                            Array.from(document.images).reduce((count, img) => {
                                try {
                                    return count + (img.complete ? 1 : 0);
                                } catch (e) {
                                    return count + 1; // Si hay error, contar como cargada
                                }
                            }, 0)
                        ");
                        
                        _logger.LogDebug("Images: {Total}, Loaded: {Loaded}", imageCount, loadedImages);
                        
                        if (loadedImages >= imageCount)
                        {
                            _logger.LogDebug("All images loaded successfully");
                            return;
                        }
                        
                        await Task.Delay(delayMs);
                        attemptCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error checking image status on attempt {Attempt}", attemptCount + 1);
                        // Continuar con el siguiente intento o salir si hay demasiados errores
                        if (attemptCount > maxAttempts / 2)
                        {
                            _logger.LogWarning("Too many errors checking images, continuing with PDF generation");
                            return;
                        }
                        
                        attemptCount++;
                        await Task.Delay(delayMs);
                    }
                }
                
                _logger.LogWarning("Timeout waiting for images to load after {Attempts} attempts", maxAttempts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in WaitForImagesAsync - continuing with PDF generation");
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
