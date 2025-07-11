using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Abstractions
{
    /// <summary>
    /// Main Html2Pdf service interface
    /// </summary>
    public interface IHtml2PdfService
    {
        /// <summary>
        /// Generates PDF from an HTML template and data
        /// </summary>
        Task<PdfResult> GeneratePdfAsync(string template, object? model = null, PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null);

        /// <summary>
        /// Generates PDF from an HTML template file
        /// </summary>
        Task<PdfResult> GeneratePdfFromFileAsync(string templatePath, object? model = null, PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null);

        /// <summary>
        /// Generates PDF from direct HTML
        /// </summary>
        Task<PdfResult> GeneratePdfFromHtmlAsync(string html, PdfOptions? pdfOptions = null);

        /// <summary>
        /// Generates PDF from a URL
        /// </summary>
        Task<PdfResult> GeneratePdfFromUrlAsync(string url, PdfOptions? pdfOptions = null);
    }
}
