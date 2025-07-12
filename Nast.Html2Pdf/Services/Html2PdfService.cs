using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Models;
using System.Diagnostics;

namespace Nast.Html2Pdf.Services
{
    /// <summary>
    /// Main service for HTML to PDF conversion with enhanced diagnostics
    /// </summary>
    internal sealed class Html2PdfService : IHtml2PdfService
    {
        private readonly IHtmlGenerator _htmlGenerator;
        private readonly IPdfConverter _pdfConverter;
        private readonly ILogger<Html2PdfService> _logger;
        private readonly Html2PdfDiagnostics _diagnostics;

        public Html2PdfService(
            IHtmlGenerator htmlGenerator,
            IPdfConverter pdfConverter,
            ILogger<Html2PdfService> logger,
            Html2PdfDiagnostics diagnostics)
        {
            _htmlGenerator = htmlGenerator;
            _pdfConverter = pdfConverter;
            _logger = logger;
            _diagnostics = diagnostics;
        }

        public async Task<PdfResult> GeneratePdfAsync(string template, object? model = null,
            PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null)
        {
            using var operation = _diagnostics.BeginPdfGeneration("Template", new { 
                TemplateLength = template.Length, 
                HasModel = model != null,
                PdfOptions = pdfOptions,
                HtmlOptions = htmlOptions 
            });

            var stopwatch = Stopwatch.StartNew();
            var performanceMetrics = new PerformanceMetrics();
            var operationId = Guid.NewGuid().ToString();

            try
            {
                _logger.LogDebug("Starting PDF generation from template | Operation ID: {OperationId}", operationId);

                // Generate HTML with detailed timing
                var htmlStopwatch = Stopwatch.StartNew();
                var htmlResult = await _htmlGenerator.GenerateAsync(template, model, htmlOptions);
                htmlStopwatch.Stop();
                var htmlDuration = htmlStopwatch.Elapsed;
                
                performanceMetrics.HtmlGenerationTime = htmlDuration;
                _diagnostics.LogTemplateProcessing(operationId, "Razor", template.Length, htmlDuration, htmlResult.Success);

                if (!htmlResult.Success)
                {
                    stopwatch.Stop();
                    performanceMetrics.TotalTime = stopwatch.Elapsed;
                    _diagnostics.LogDetailedError(operationId, htmlResult.Exception!, "HTML Generation", new { Template = template, Model = model });
                    return PdfResult.CreateError($"HTML generation failed: {htmlResult.ErrorMessage}",
                        htmlResult.Exception, stopwatch.Elapsed);
                }

                performanceMetrics.HtmlSize = htmlResult.Html!.Length;
                _diagnostics.LogHtmlGenerationMetrics(operationId, htmlDuration, htmlResult.Html!.Length, true);

                // Convert to PDF with detailed timing
                var pdfStopwatch = Stopwatch.StartNew();
                var pdfResult = await _pdfConverter.ConvertAsync(htmlResult.Html!, pdfOptions);
                pdfStopwatch.Stop();
                var pdfDuration = pdfStopwatch.Elapsed;
                
                performanceMetrics.PdfConversionTime = pdfDuration;

                if (!pdfResult.Success)
                {
                    stopwatch.Stop();
                    performanceMetrics.TotalTime = stopwatch.Elapsed;
                    _diagnostics.LogDetailedError(operationId, pdfResult.Exception!, "PDF Conversion", new { Html = htmlResult.Html, Options = pdfOptions });
                    return PdfResult.CreateError($"PDF conversion failed: {pdfResult.ErrorMessage}",
                        pdfResult.Exception, stopwatch.Elapsed);
                }

                stopwatch.Stop();
                performanceMetrics.TotalTime = stopwatch.Elapsed;
                performanceMetrics.PdfSize = pdfResult.Data!.Length;
                performanceMetrics.MemoryUsage = GC.GetTotalMemory(false);
                performanceMetrics.CpuUsage = 0; // Simplified for now
                performanceMetrics.BottleneckIdentified = performanceMetrics.IdentifyBottleneck();

                _diagnostics.LogPdfConversionMetrics(operationId, pdfDuration, pdfResult.Data!.Length, true);
                _diagnostics.LogResourceUsage(operationId, performanceMetrics.MemoryUsage, performanceMetrics.CpuUsage, performanceMetrics.TotalTime);

                if (performanceMetrics.BottleneckIdentified != "No significant bottleneck detected")
                {
                    _diagnostics.LogPerformanceBottleneck(operationId, performanceMetrics.BottleneckIdentified, 
                        performanceMetrics.TotalTime, $"HTML: {htmlDuration.TotalMilliseconds}ms, PDF: {pdfDuration.TotalMilliseconds}ms");
                }

                _logger.LogDebug("PDF generation completed successfully | Operation ID: {OperationId} | Duration: {Duration}ms", 
                    operationId, stopwatch.ElapsedMilliseconds);

                return PdfResult.CreateSuccess(pdfResult.Data!, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                performanceMetrics.TotalTime = stopwatch.Elapsed;
                _diagnostics.LogDetailedError(operationId, ex, "PDF Generation", new { Template = template, Model = model, Options = pdfOptions });
                _logger.LogError(ex, "Error generating PDF from template | Operation ID: {OperationId}", operationId);
                return PdfResult.CreateError($"Error generating PDF: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        public async Task<PdfResult> GeneratePdfFromFileAsync(string templatePath, object? model = null,
            PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Starting PDF generation from file: {TemplatePath}", templatePath);

                // Generar HTML
                var htmlResult = await _htmlGenerator.GenerateFromFileAsync(templatePath, model, htmlOptions);
                if (!htmlResult.Success)
                {
                    stopwatch.Stop();
                    return PdfResult.CreateError($"HTML generation failed: {htmlResult.ErrorMessage}",
                        htmlResult.Exception, stopwatch.Elapsed);
                }

                // Convertir a PDF
                var pdfResult = await _pdfConverter.ConvertAsync(htmlResult.Html!, pdfOptions);
                if (!pdfResult.Success)
                {
                    stopwatch.Stop();
                    return PdfResult.CreateError($"PDF conversion failed: {pdfResult.ErrorMessage}",
                        pdfResult.Exception, stopwatch.Elapsed);
                }

                stopwatch.Stop();
                _logger.LogDebug("PDF generation from file completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return PdfResult.CreateSuccess(pdfResult.Data!, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating PDF from file: {TemplatePath}", templatePath);
                return PdfResult.CreateError($"Error generating PDF from file: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        public async Task<PdfResult> GeneratePdfFromHtmlAsync(string html, PdfOptions? pdfOptions = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Starting PDF generation from HTML");

                // Convertir directamente a PDF
                var pdfResult = await _pdfConverter.ConvertAsync(html, pdfOptions);
                if (!pdfResult.Success)
                {
                    stopwatch.Stop();
                    return PdfResult.CreateError($"PDF conversion failed: {pdfResult.ErrorMessage}",
                        pdfResult.Exception, stopwatch.Elapsed);
                }

                stopwatch.Stop();
                _logger.LogDebug("PDF generation from HTML completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return PdfResult.CreateSuccess(pdfResult.Data!, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating PDF from HTML");
                return PdfResult.CreateError($"Error generating PDF from HTML: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }

        public async Task<PdfResult> GeneratePdfFromUrlAsync(string url, PdfOptions? pdfOptions = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Starting PDF generation from URL: {Url}", url);

                // Convertir directamente a PDF
                var pdfResult = await _pdfConverter.ConvertFromUrlAsync(url, pdfOptions);
                if (!pdfResult.Success)
                {
                    stopwatch.Stop();
                    return PdfResult.CreateError($"PDF conversion failed: {pdfResult.ErrorMessage}",
                        pdfResult.Exception, stopwatch.Elapsed);
                }

                stopwatch.Stop();
                _logger.LogDebug("PDF generation from URL completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return PdfResult.CreateSuccess(pdfResult.Data!, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating PDF from URL: {Url}", url);
                return PdfResult.CreateError($"Error generating PDF from URL: {ex.Message}", ex, stopwatch.Elapsed);
            }
        }
    }
}
