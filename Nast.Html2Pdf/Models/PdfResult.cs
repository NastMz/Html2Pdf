namespace Nast.Html2Pdf.Models
{
    /// <summary>
    /// Result of PDF generation
    /// </summary>
    public sealed class PdfResult
    {
        /// <summary>
        /// Indicates if generation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Generated PDF data
        /// </summary>
        public byte[]? Data { get; set; }

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
        /// Size of generated PDF in bytes
        /// </summary>
        public int Size => Data?.Length ?? 0;

        /// <summary>
        /// Creates a successful result
        /// </summary>
        public static PdfResult CreateSuccess(byte[] data, TimeSpan duration)
        {
            return new PdfResult
            {
                Success = true,
                Data = data,
                Duration = duration
            };
        }

        /// <summary>
        /// Creates an error result
        /// </summary>
        public static PdfResult CreateError(string message, Exception? exception = null, TimeSpan duration = default)
        {
            return new PdfResult
            {
                Success = false,
                ErrorMessage = message,
                Exception = exception,
                Duration = duration
            };
        }
    }
}
