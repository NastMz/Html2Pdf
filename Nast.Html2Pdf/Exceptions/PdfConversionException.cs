namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Represents errors that occur during PDF conversion processes.
    /// </summary>
    public class PdfConversionException : Html2PdfException
    {
        public PdfConversionException(string message) : base(message) { }
        public PdfConversionException(string message, Exception innerException) : base(message, innerException) { }
    }
}
