namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Represents errors that occur within the browser pool operations.
    /// </summary>
    public class BrowserPoolException : Html2PdfException
    {
        public BrowserPoolException(string message) : base(message) { }
        public BrowserPoolException(string message, Exception innerException) : base(message, innerException) { }
    }
}
