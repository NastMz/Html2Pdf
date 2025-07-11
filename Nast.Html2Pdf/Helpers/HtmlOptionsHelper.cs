using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Helpers
{
    /// <summary>
    /// Helper for creating predefined HTML configurations
    /// </summary>
    public static class HtmlOptionsHelper
    {
        private const string DefaultEncoding = "UTF-8";

        /// <summary>
        /// Standard configuration for HTML
        /// </summary>
        public static HtmlGenerationOptions Standard => new()
        {
            InlineStyles = true,
            Encoding = DefaultEncoding,
            IncludeViewport = true,
            AdditionalCss = @"
                body { 
                    font-family: Arial, sans-serif; 
                    margin: 0; 
                    padding: 20px; 
                    font-size: 12px; 
                    line-height: 1.4; 
                }
                h1, h2, h3 { margin-top: 0; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                th { background-color: #f2f2f2; }
                .page-break { page-break-after: always; }
            "
        };

        /// <summary>
        /// Configuration for invoices
        /// </summary>
        public static HtmlGenerationOptions Invoice => new()
        {
            InlineStyles = true,
            Encoding = DefaultEncoding,
            IncludeViewport = true,
            AdditionalCss = @"
                body { 
                    font-family: Arial, sans-serif; 
                    margin: 0; 
                    padding: 0; 
                    font-size: 11px; 
                    line-height: 1.3; 
                }
                .invoice-header { text-align: center; margin-bottom: 30px; }
                .invoice-details { margin-bottom: 20px; }
                .invoice-table { width: 100%; border-collapse: collapse; }
                .invoice-table th, .invoice-table td { 
                    border: 1px solid #ddd; 
                    padding: 8px; 
                    text-align: left; 
                }
                .invoice-table th { 
                    background-color: #f8f9fa; 
                    font-weight: bold; 
                }
                .invoice-total { 
                    text-align: right; 
                    margin-top: 20px; 
                    font-weight: bold; 
                }
            "
        };

        /// <summary>
        /// Configuration for reports
        /// </summary>
        public static HtmlGenerationOptions Report => new()
        {
            InlineStyles = true,
            Encoding = DefaultEncoding,
            IncludeViewport = true,
            AdditionalCss = @"
                body { 
                    font-family: Arial, sans-serif; 
                    margin: 0; 
                    padding: 0; 
                    font-size: 11px; 
                    line-height: 1.4; 
                }
                .report-header { 
                    text-align: center; 
                    margin-bottom: 30px; 
                    border-bottom: 2px solid #333; 
                    padding-bottom: 10px; 
                }
                .report-section { 
                    margin-bottom: 25px; 
                }
                .report-table { 
                    width: 100%; 
                    border-collapse: collapse; 
                    margin-bottom: 20px; 
                }
                .report-table th, .report-table td { 
                    border: 1px solid #ddd; 
                    padding: 6px; 
                    text-align: left; 
                }
                .report-table th { 
                    background-color: #f8f9fa; 
                    font-weight: bold; 
                }
                .report-summary { 
                    background-color: #f8f9fa; 
                    padding: 15px; 
                    border-radius: 5px; 
                }
            "
        };

        /// <summary>
        /// Configuration with custom CSS
        /// </summary>
        public static HtmlGenerationOptions WithCustomCss(string customCss)
        {
            return new HtmlGenerationOptions
            {
                InlineStyles = true,
                Encoding = DefaultEncoding,
                IncludeViewport = true,
                AdditionalCss = customCss
            };
        }
    }
}
