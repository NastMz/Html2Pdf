using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Interfaces
{
    /// <summary>
    /// Interface for HTML generation using templates
    /// </summary>
    public interface IHtmlGenerator
    {
        /// <summary>
        /// Generates HTML from a template and data
        /// </summary>
        Task<HtmlResult> GenerateAsync(string template, object? model = null, HtmlGenerationOptions? options = null);

        /// <summary>
        /// Generates HTML from a template file
        /// </summary>
        Task<HtmlResult> GenerateFromFileAsync(string templatePath, object? model = null, HtmlGenerationOptions? options = null);

        /// <summary>
        /// Generates HTML from an embedded template
        /// </summary>
        Task<HtmlResult> GenerateFromResourceAsync(string resourceKey, object? model = null, HtmlGenerationOptions? options = null);
    }

    /// <summary>
    /// Interface for HTML to PDF conversion
    /// </summary>
    public interface IPdfConverter
    {
        /// <summary>
        /// Converts HTML to PDF
        /// </summary>
        Task<PdfResult> ConvertAsync(string html, PdfOptions? options = null);

        /// <summary>
        /// Converts a URL to PDF
        /// </summary>
        Task<PdfResult> ConvertFromUrlAsync(string url, PdfOptions? options = null);
    }

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

    /// <summary>
    /// Interface for browser pool
    /// </summary>
    public interface IBrowserPool : IDisposable
    {
        /// <summary>
        /// Gets a page from the pool
        /// </summary>
        Task<IPooledPage> GetPageAsync();

        /// <summary>
        /// Returns a page to the pool
        /// </summary>
        Task ReturnPageAsync(IPooledPage page);
    }

    /// <summary>
    /// Interface for a pooled page
    /// </summary>
    public interface IPooledPage : IDisposable
    {
        /// <summary>
        /// Playwright page
        /// </summary>
        Microsoft.Playwright.IPage Page { get; }

        /// <summary>
        /// Indicates if the page is in use
        /// </summary>
        bool IsInUse { get; }

        /// <summary>
        /// Marks the page as in use
        /// </summary>
        void MarkAsInUse();

        /// <summary>
        /// Marks the page as available
        /// </summary>
        void MarkAsAvailable();
    }
}
