namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Represents an exception that occurs when there is an issue with a resource during the HTML to PDF conversion
    /// process.
    /// </summary>
    public class ResourceException : Html2PdfException
    {
        public ResourceException(string message) : base(message) { }
        public ResourceException(string message, Exception innerException) : base(message, innerException) { }
    }
}
