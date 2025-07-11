using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Abstractions
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
}
