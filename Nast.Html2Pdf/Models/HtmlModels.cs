namespace Nast.Html2Pdf.Models
{
    /// <summary>
    /// Configuration for HTML generation
    /// </summary>
    public sealed class HtmlGenerationOptions
    {
        /// <summary>
        /// Base path for resolving relative resources
        /// </summary>
        public string? BasePath { get; set; }

        /// <summary>
        /// Include inline CSS styles
        /// </summary>
        public bool InlineStyles { get; set; } = true;

        /// <summary>
        /// HTML encoding
        /// </summary>
        public string Encoding { get; set; } = "UTF-8";

        /// <summary>
        /// Include default meta viewport
        /// </summary>
        public bool IncludeViewport { get; set; } = true;

        /// <summary>
        /// Additional CSS styles
        /// </summary>
        public string? AdditionalCss { get; set; }

        /// <summary>
        /// Additional JavaScript scripts
        /// </summary>
        public string? AdditionalJs { get; set; }
    }

    /// <summary>
    /// Result of HTML generation
    /// </summary>
    public sealed class HtmlResult
    {
        /// <summary>
        /// Indicates if generation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Generated HTML
        /// </summary>
        public string? Html { get; set; }

        /// <summary>
        /// Error message if generation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Detailed exception if an error occurred
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Time taken for generation
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Creates a successful result
        /// </summary>
        public static HtmlResult CreateSuccess(string html, TimeSpan duration)
        {
            return new HtmlResult
            {
                Success = true,
                Html = html,
                Duration = duration
            };
        }

        /// <summary>
        /// Creates an error result
        /// </summary>
        public static HtmlResult CreateError(string message, Exception? exception = null, TimeSpan duration = default)
        {
            return new HtmlResult
            {
                Success = false,
                ErrorMessage = message,
                Exception = exception,
                Duration = duration
            };
        }
    }
}
