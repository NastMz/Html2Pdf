namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Represents errors that occur during HTML generation.
    /// </summary>
    public class HtmlGenerationException : Html2PdfException
    {
        public HtmlGenerationException(string message) : base(message) { }
        public HtmlGenerationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
