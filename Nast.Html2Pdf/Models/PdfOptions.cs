using Microsoft.Playwright;

namespace Nast.Html2Pdf.Models
{
    /// <summary>
    /// Configuration for PDF generation
    /// </summary>
    public sealed class PdfOptions
    {
        /// <summary>
        /// Page format (A4, A3, Letter, etc.)
        /// </summary>
        public string Format { get; set; } = "A4";

        /// <summary>
        /// Page orientation
        /// </summary>
        public bool Landscape { get; set; } = false;

        /// <summary>
        /// Page margins
        /// </summary>
        public PdfMargins Margins { get; set; } = new();

        /// <summary>
        /// Page header
        /// </summary>
        public PdfHeaderFooter? Header { get; set; }

        /// <summary>
        /// Page footer
        /// </summary>
        public PdfHeaderFooter? Footer { get; set; }

        /// <summary>
        /// Display background graphics
        /// </summary>
        public bool PrintBackground { get; set; } = true;

        /// <summary>
        /// Page scale (0.1 to 2.0)
        /// </summary>
        public float Scale { get; set; } = 1.0f;

        /// <summary>
        /// Custom width (in inches)
        /// </summary>
        public float? Width { get; set; }

        /// <summary>
        /// Custom height (in inches)
        /// </summary>
        public float? Height { get; set; }

        /// <summary>
        /// Page ranges to print
        /// </summary>
        public string? PageRanges { get; set; }

        /// <summary>
        /// Preferencias de color
        /// </summary>
        public ColorScheme ColorScheme { get; set; } = ColorScheme.Light;

        /// <summary>
        /// Esperar a que se carguen las imágenes
        /// </summary>
        public bool WaitForImages { get; set; } = true;

        /// <summary>
        /// Page load timeout (in milliseconds)
        /// </summary>
        public int TimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// PDF margins
    /// </summary>
    public sealed class PdfMargins
    {
        /// <summary>
        /// Top margin
        /// </summary>
        public string Top { get; set; } = "1cm";

        /// <summary>
        /// Bottom margin
        /// </summary>
        public string Bottom { get; set; } = "1cm";

        /// <summary>
        /// Left margin
        /// </summary>
        public string Left { get; set; } = "1cm";

        /// <summary>
        /// Right margin
        /// </summary>
        public string Right { get; set; } = "1cm";
    }

    /// <summary>
    /// Header or footer
    /// </summary>
    public sealed class PdfHeaderFooter
    {
        /// <summary>
        /// HTML template for header/footer
        /// </summary>
        public string Template { get; set; } = string.Empty;

        /// <summary>
        /// Data for the template
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Height of header/footer
        /// </summary>
        public string Height { get; set; } = "1cm";
    }
}
