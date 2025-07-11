using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Nast.Html2Pdf.Interfaces;
using Nast.Html2Pdf.Models;
using Nast.Html2Pdf.Exceptions;

namespace Nast.Html2Pdf.Services
{
    /// <summary>
    /// Main service for HTML to PDF conversion
    /// </summary>
    internal sealed class Html2PdfService : IHtml2PdfService
    {
        private readonly IHtmlGenerator _htmlGenerator;
        private readonly IPdfConverter _pdfConverter;
        private readonly ILogger<Html2PdfService> _logger;

        public Html2PdfService(
            IHtmlGenerator htmlGenerator,
            IPdfConverter pdfConverter,
            ILogger<Html2PdfService> logger)
        {
            _htmlGenerator = htmlGenerator;
            _pdfConverter = pdfConverter;
            _logger = logger;
        }

        public async Task<PdfResult> GeneratePdfAsync(string template, object? model = null, 
            PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.LogDebug("Starting PDF generation from template");

                // Generar HTML
                var htmlResult = await _htmlGenerator.GenerateAsync(template, model, htmlOptions);
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
                _logger.LogDebug("PDF generation completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

                return PdfResult.CreateSuccess(pdfResult.Data!, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generating PDF from template");
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
