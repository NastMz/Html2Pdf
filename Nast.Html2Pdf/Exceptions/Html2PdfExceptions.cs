namespace Nast.Html2Pdf.Exceptions
{
    /// <summary>
    /// Excepción base para errores de Html2Pdf
    /// </summary>
    public class Html2PdfException : Exception
    {
        public Html2PdfException(string message) : base(message) { }
        public Html2PdfException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Excepción para errores de generación de HTML
    /// </summary>
    public class HtmlGenerationException : Html2PdfException
    {
        public HtmlGenerationException(string message) : base(message) { }
        public HtmlGenerationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Excepción para errores de conversión a PDF
    /// </summary>
    public class PdfConversionException : Html2PdfException
    {
        public PdfConversionException(string message) : base(message) { }
        public PdfConversionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Excepción para errores de plantilla
    /// </summary>
    public class TemplateException : Html2PdfException
    {
        public TemplateException(string message) : base(message) { }
        public TemplateException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Excepción para errores de recursos
    /// </summary>
    public class ResourceException : Html2PdfException
    {
        public ResourceException(string message) : base(message) { }
        public ResourceException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Excepción para errores del pool de navegadores
    /// </summary>
    public class BrowserPoolException : Html2PdfException
    {
        public BrowserPoolException(string message) : base(message) { }
        public BrowserPoolException(string message, Exception innerException) : base(message, innerException) { }
    }
}
