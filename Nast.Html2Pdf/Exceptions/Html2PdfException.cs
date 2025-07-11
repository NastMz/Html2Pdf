namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Base exception for Html2Pdf errors
    /// </summary>
    public class Html2PdfException : Exception
    {
        public Html2PdfException(string message) : base(message) { }
        public Html2PdfException(string message, Exception innerException) : base(message, innerException) { }
    }
}
