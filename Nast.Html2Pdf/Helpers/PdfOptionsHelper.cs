using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Helpers
{
    /// <summary>
    /// Helper for creating predefined PDF configurations
    /// </summary>
    public static class PdfOptionsHelper
    {

        /// <summary>
        /// Configuration for standard A4 documents
        /// </summary>
        public static PdfOptions A4Standard => new()
        {
            Format = "A4",
            Landscape = false,
            Margins = new PdfMargins
            {
                Top = "2cm",
                Bottom = "2cm",
                Left = "2cm",
                Right = "2cm"
            },
            PrintBackground = true,
            Scale = 1.0f
        };

        /// <summary>
        /// Configuration for A4 landscape documents
        /// </summary>
        public static PdfOptions A4Landscape => new()
        {
            Format = "A4",
            Landscape = true,
            Margins = new PdfMargins
            {
                Top = "2cm",
                Bottom = "2cm",
                Left = "2cm",
                Right = "2cm"
            },
            PrintBackground = true,
            Scale = 1.0f
        };

        /// <summary>
        /// Configuration for standard Letter documents
        /// </summary>
        public static PdfOptions LetterStandard => new()
        {
            Format = "Letter",
            Landscape = false,
            Margins = new PdfMargins
            {
                Top = "1in",
                Bottom = "1in",
                Left = "1in",
                Right = "1in"
            },
            PrintBackground = true,
            Scale = 1.0f
        };

        /// <summary>
        /// Configuration for documents without margins
        /// </summary>
        public static PdfOptions NoMargins => new()
        {
            Format = "A4",
            Landscape = false,
            Margins = new PdfMargins
            {
                Top = "0",
                Bottom = "0",
                Left = "0",
                Right = "0"
            },
            PrintBackground = true,
            Scale = 1.0f
        };

        /// <summary>
        /// Configuration for invoices
        /// </summary>
        public static PdfOptions Invoice => new()
        {
            Format = "A4",
            Landscape = false,
            Margins = new PdfMargins
            {
                Top = "1cm",
                Bottom = "3cm",
                Left = "2cm",
                Right = "2cm"
            },
            PrintBackground = true,
            Scale = 1.0f,
            Footer = new PdfHeaderFooter
            {
                Template = @"
                    <div style='font-size: 10px; text-align: center; width: 100%; color: #666;'>
                        <span>Page <span class='pageNumber'></span> of <span class='totalPages'></span></span>
                    </div>",
                Height = "1cm"
            }
        };

        /// <summary>
        /// Configuration for reports
        /// </summary>
        public static PdfOptions Report => new()
        {
            Format = "A4",
            Landscape = false,
            Margins = new PdfMargins
            {
                Top = "3cm",
                Bottom = "2cm",
                Left = "2cm",
                Right = "2cm"
            },
            PrintBackground = true,
            Scale = 1.0f,
            Header = new PdfHeaderFooter
            {
                Template = @"
                    <div style='font-size: 12px; text-align: center; width: 100%; border-bottom: 1px solid #ccc; padding-bottom: 10px;'>
                        <strong>REPORT</strong>
                    </div>",
                Height = "2cm"
            },
            Footer = new PdfHeaderFooter
            {
                Template = @"
                    <div style='font-size: 10px; width: 100%; color: #666; display: flex; justify-content: space-between;'>
                        <span>Generated on: <span class='date'></span></span>
                        <span>Page <span class='pageNumber'></span> of <span class='totalPages'></span></span>
                    </div>",
                Height = "1cm"
            }
        };

        /// <summary>
        /// Creates a custom configuration with header and footer
        /// </summary>
        public static PdfOptions WithHeaderAndFooter(string? headerTemplate = null, string? footerTemplate = null)
        {
            var options = A4Standard;

            if (!string.IsNullOrEmpty(headerTemplate))
            {
                options.Header = new PdfHeaderFooter
                {
                    Template = headerTemplate,
                    Height = "2cm"
                };
                options.Margins.Top = "3cm";
            }

            if (!string.IsNullOrEmpty(footerTemplate))
            {
                options.Footer = new PdfHeaderFooter
                {
                    Template = footerTemplate,
                    Height = "2cm"
                };
                options.Margins.Bottom = "3cm";
            }

            return options;
        }

        /// <summary>
        /// Creates a configuration with custom dimensions
        /// </summary>
        public static PdfOptions WithCustomSize(float widthInches, float heightInches)
        {
            return new PdfOptions
            {
                Width = widthInches,
                Height = heightInches,
                Margins = new PdfMargins
                {
                    Top = "1cm",
                    Bottom = "1cm",
                    Left = "1cm",
                    Right = "1cm"
                },
                PrintBackground = true,
                Scale = 1.0f
            };
        }
    }
}
