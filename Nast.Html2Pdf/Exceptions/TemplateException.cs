namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Represents errors that occur during template processing in the HTML to PDF conversion.
    /// </summary>
    public class TemplateException : Html2PdfException
    {
        public TemplateException(string message) : base(message) { }
        public TemplateException(string message, Exception innerException) : base(message, innerException) { }
    }
}
