using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Abstractions
{
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
}
