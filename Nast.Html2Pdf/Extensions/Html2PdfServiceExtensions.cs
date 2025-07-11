using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Extensions
{
    /// <summary>
    /// Provides extension methods for generating PDF documents using the <see cref="IHtml2PdfService"/>.
    /// </summary>
    public static class Html2PdfServiceExtensions
    {
        /// <summary>
        /// Generates a PDF from the specified HTML template and writes it to a file asynchronously.
        /// </summary>
        /// <remarks>This method uses the specified <paramref name="service"/> to generate a PDF from the
        /// given <paramref name="template"/> and writes the resulting PDF to the specified <paramref
        /// name="outputPath"/>. Ensure that the <paramref name="outputPath"/> is accessible and writable.</remarks>
        /// <param name="service">The HTML to PDF conversion service used to generate the PDF.</param>
        /// <param name="template">The HTML template used for generating the PDF. Cannot be null or empty.</param>
        /// <param name="model">An optional model to be used for template data binding. Can be null.</param>
        /// <param name="outputPath">The file path where the generated PDF will be saved. Cannot be null or empty.</param>
        /// <param name="pdfOptions">Optional PDF generation options. Can be null.</param>
        /// <param name="htmlOptions">Optional HTML generation options. Can be null.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains <see langword="true"/> if the
        /// PDF was successfully generated and written to the file; otherwise, <see langword="false"/>.</returns>
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
        /// Asynchronously generates a PDF from an HTML template file and writes it to a specified output file.
        /// </summary>
        /// <remarks>This method uses the specified <paramref name="service"/> to convert an HTML template
        /// into a PDF document. The generated PDF is then written to the specified <paramref name="outputPath"/>.
        /// Ensure that the output directory exists and is writable.</remarks>
        /// <param name="service">The HTML to PDF conversion service used to generate the PDF.</param>
        /// <param name="templatePath">The file path to the HTML template used for generating the PDF. Cannot be null or empty.</param>
        /// <param name="model">An optional data model to be used for populating the HTML template. Can be null if no model is required.</param>
        /// <param name="outputPath">The file path where the generated PDF will be saved. Cannot be null or empty.</param>
        /// <param name="pdfOptions">Optional PDF generation options that customize the output PDF. Can be null to use default options.</param>
        /// <param name="htmlOptions">Optional HTML generation options that affect how the HTML is processed before conversion. Can be null to use
        /// default options.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains <see langword="true"/> if the
        /// PDF was successfully generated and written to the output file; otherwise, <see langword="false"/>.</returns>
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
        /// Asynchronously generates a PDF from the specified HTML content and writes it to a file.
        /// </summary>
        /// <remarks>This method uses the specified <paramref name="service"/> to convert HTML content
        /// into a PDF document. Ensure that the <paramref name="outputPath"/> is accessible and that the application
        /// has write permissions.</remarks>
        /// <param name="service">The HTML to PDF conversion service used to generate the PDF.</param>
        /// <param name="html">The HTML content to convert into a PDF document. Cannot be null or empty.</param>
        /// <param name="outputPath">The file path where the generated PDF will be saved. Cannot be null or empty.</param>
        /// <param name="pdfOptions">Optional settings for PDF generation, such as page size and margins. Can be null to use default options.</param>
        /// <returns><see langword="true"/> if the PDF was successfully generated and written to the specified file; otherwise,
        /// <see langword="false"/>.</returns>
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
        /// Asynchronously generates a PDF from the specified URL and writes it to a file.
        /// </summary>
        /// <remarks>This method uses the specified <paramref name="service"/> to convert the web page at
        /// the given <paramref name="url"/> into a PDF document. The resulting PDF is then saved to the specified
        /// <paramref name="outputPath"/>. If the conversion fails, the method returns <see
        /// langword="false"/>.</remarks>
        /// <param name="service">The HTML to PDF conversion service used to generate the PDF.</param>
        /// <param name="url">The URL of the web page to convert to a PDF. Cannot be null or empty.</param>
        /// <param name="outputPath">The file path where the generated PDF will be saved. Cannot be null or empty.</param>
        /// <param name="pdfOptions">Optional PDF generation options that customize the output. Can be null.</param>
        /// <returns><see langword="true"/> if the PDF was successfully generated and written to the file; otherwise, <see
        /// langword="false"/>.</returns>
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
        /// Generates a PDF document from the specified HTML template and model, and returns it as a stream.
        /// </summary>
        /// <remarks>This method uses the specified <paramref name="service"/> to convert the HTML
        /// template into a PDF document. If the conversion is successful, the PDF data is returned as a <see
        /// cref="MemoryStream"/>.</remarks>
        /// <param name="service">The HTML to PDF conversion service used to generate the PDF.</param>
        /// <param name="template">The HTML template to be converted into a PDF document.</param>
        /// <param name="model">The data model to be applied to the HTML template. Can be <see langword="null"/> if no model is needed.</param>
        /// <param name="pdfOptions">Optional PDF generation options that influence the output format and settings. Can be <see langword="null"/>
        /// to use default options.</param>
        /// <param name="htmlOptions">Optional HTML generation options that affect how the HTML is processed before conversion. Can be <see
        /// langword="null"/> to use default options.</param>
        /// <returns>A <see cref="Stream"/> containing the generated PDF document if the operation is successful; otherwise, <see
        /// langword="null"/>.</returns>
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
