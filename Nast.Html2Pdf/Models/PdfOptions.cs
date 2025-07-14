namespace Nast.Html2Pdf.Models
{
    /// <summary>
    /// Configuration for PDF generation
    /// </summary>
    public sealed class PdfOptions : IEquatable<PdfOptions>
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
        /// Esperar a que se carguen las imágenes
        /// </summary>
        public bool WaitForImages { get; set; } = true;

        /// <summary>
        /// Page load timeout (in milliseconds)
        /// </summary>
        public int TimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Determines whether the specified PdfOptions is equal to the current PdfOptions.
        /// </summary>
        /// <param name="other">The PdfOptions to compare with the current PdfOptions.</param>
        /// <returns>true if the specified PdfOptions is equal to the current PdfOptions; otherwise, false.</returns>
        public bool Equals(PdfOptions? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return Format == other.Format &&
                   Landscape == other.Landscape &&
                   Equals(Margins, other.Margins) &&
                   Equals(Header, other.Header) &&
                   Equals(Footer, other.Footer) &&
                   PrintBackground == other.PrintBackground &&
                   Scale.Equals(other.Scale) &&
                   Nullable.Equals(Width, other.Width) &&
                   Nullable.Equals(Height, other.Height) &&
                   PageRanges == other.PageRanges &&
                   WaitForImages == other.WaitForImages &&
                   TimeoutMs == other.TimeoutMs;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as PdfOptions);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Format);
            hash.Add(Landscape);
            hash.Add(Margins);
            hash.Add(Header);
            hash.Add(Footer);
            hash.Add(PrintBackground);
            hash.Add(Scale);
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(PageRanges);
            hash.Add(WaitForImages);
            hash.Add(TimeoutMs);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// PDF margins
    /// </summary>
    public sealed class PdfMargins : IEquatable<PdfMargins>
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

        /// <summary>
        /// Determines whether the specified PdfMargins is equal to the current PdfMargins.
        /// </summary>
        /// <param name="other">The PdfMargins to compare with the current PdfMargins.</param>
        /// <returns>true if the specified PdfMargins is equal to the current PdfMargins; otherwise, false.</returns>
        public bool Equals(PdfMargins? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return Top == other.Top &&
                   Bottom == other.Bottom &&
                   Left == other.Left &&
                   Right == other.Right;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as PdfMargins);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Top, Bottom, Left, Right);
        }
    }

    /// <summary>
    /// Header or footer
    /// </summary>
    public sealed class PdfHeaderFooter : IEquatable<PdfHeaderFooter>
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

        /// <summary>
        /// Determines whether the specified PdfHeaderFooter is equal to the current PdfHeaderFooter.
        /// </summary>
        /// <param name="other">The PdfHeaderFooter to compare with the current PdfHeaderFooter.</param>
        /// <returns>true if the specified PdfHeaderFooter is equal to the current PdfHeaderFooter; otherwise, false.</returns>
        public bool Equals(PdfHeaderFooter? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return Template == other.Template &&
                   Equals(Data, other.Data) &&
                   Height == other.Height;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as PdfHeaderFooter);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Template, Data, Height);
        }
    }
}
