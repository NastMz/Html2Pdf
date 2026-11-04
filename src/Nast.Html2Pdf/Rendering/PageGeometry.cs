using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using PuppeteerSharp.Media;
using PuppeteerPdfOptions = PuppeteerSharp.PdfOptions;

namespace Nast.Html2Pdf.Rendering;

/// <summary>Validated page geometry of a conversion and its translation to Chromium print settings.</summary>
internal sealed partial class PageGeometry
{
    private const double MinimumContentMillimeters = 10;

    private PageGeometry(PdfConversionOptions options)
    {
        Options = options;
        var portraitWidth = options.PaperSize.Width.ToMillimeters();
        var portraitHeight = options.PaperSize.Height.ToMillimeters();
        (WidthMm, HeightMm) = options.Orientation == PageOrientation.Landscape
            ? (portraitHeight, portraitWidth)
            : (portraitWidth, portraitHeight);
    }

    public PdfConversionOptions Options { get; }

    public double WidthMm { get; }

    public double HeightMm { get; }

    public PageMargins Margins => Options.Margins;

    public static PageGeometry Create(PdfConversionOptions options)
    {
        if (options.PaperSize is null || options.Margins is null || options.Readiness is null)
        {
            throw Invalid("PaperSize, Margins and Readiness must not be null.");
        }

        if (!Enum.IsDefined(options.Orientation) || !Enum.IsDefined(options.LayoutPrecedence) || !Enum.IsDefined(options.MediaType) || !Enum.IsDefined(options.ResourceFailures))
        {
            throw Invalid("An option has an undefined enumeration value.");
        }

        if (double.IsNaN(options.Scale) || options.Scale < 0.1 || options.Scale > 2)
        {
            throw Invalid("Scale must be between 0.1 and 2.");
        }

        var geometry = new PageGeometry(options);
        var m = options.Margins;
        var contentWidth = geometry.WidthMm - m.Left.ToMillimeters() - m.Right.ToMillimeters();
        var contentHeight = geometry.HeightMm - m.Top.ToMillimeters() - m.Bottom.ToMillimeters();
        if (contentWidth < MinimumContentMillimeters || contentHeight < MinimumContentMillimeters)
        {
            throw Invalid($"The margins leave less than {MinimumContentMillimeters}mm of printable width or height on a {geometry.WidthMm:0.#}mm x {geometry.HeightMm:0.#}mm page.");
        }

        if (options.Header is not null || options.Footer is not null)
        {
            if (options.LayoutPrecedence == PageLayoutPrecedence.Document)
            {
                throw Invalid("Headers and footers require LayoutPrecedence.Job, because a document-defined page size or margin could not reserve their space.");
            }

            ValidateBand(options.Header, m.Top, "Header", "top");
            ValidateBand(options.Footer, m.Bottom, "Footer", "bottom");
        }

        if (options.PageRanges is not null && !PageRangePattern().IsMatch(options.PageRanges))
        {
            throw Invalid("PageRanges must look like \"1-3, 5, 8-\" with page numbers starting at 1.");
        }

        if (options.Timeout is { } timeout && (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1)))
        {
            throw Invalid("Timeout must be positive and at most one hour.");
        }

        var readiness = options.Readiness;
        if (readiness.NetworkIdleTime is { } idle && (idle < TimeSpan.Zero || idle > TimeSpan.FromMinutes(5)))
        {
            throw Invalid("Readiness.NetworkIdleTime must be between zero and five minutes.");
        }

        if (readiness.WaitForSelector is not null && string.IsNullOrWhiteSpace(readiness.WaitForSelector))
        {
            throw Invalid("Readiness.WaitForSelector cannot be blank.");
        }

        if (readiness.WaitForExpression is not null && string.IsNullOrWhiteSpace(readiness.WaitForExpression))
        {
            throw Invalid("Readiness.WaitForExpression cannot be blank.");
        }

        return geometry;
    }

    private static void ValidateBand(PdfHeaderFooter? band, Length margin, string name, string side)
    {
        if (band is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(band.Template))
        {
            throw Invalid($"{name} template cannot be empty.");
        }

        if (band.Height.ToMillimeters() < 1)
        {
            throw Invalid($"{name} height must be at least 1mm.");
        }

        if (band.Height.ToMillimeters() > margin.ToMillimeters() + 0.001)
        {
            throw Invalid($"{name} height ({band.Height}) exceeds the {side} margin ({margin}). Increase Margins.{(side == "top" ? "Top" : "Bottom")} so the band fits without overlapping the body.");
        }
    }

    /// <summary>A final, <c>!important</c> page rule that makes the job geometry authoritative.</summary>
    public string? AuthoritativePageCss => Options.LayoutPrecedence == PageLayoutPrecedence.Job
        ? $"@page {{ size: {Mm(WidthMm)} {Mm(HeightMm)} !important; margin: {Margins.Top.ToCssMillimeters()} {Margins.Right.ToCssMillimeters()} {Margins.Bottom.ToCssMillimeters()} {Margins.Left.ToCssMillimeters()} !important; }}"
        : null;

    public PuppeteerPdfOptions ToPdfOptions(string? headerHtml, string? footerHtml) => new()
    {
        Width = Mm(WidthMm),
        Height = Mm(HeightMm),
        Landscape = false,
        PreferCSSPageSize = true,
        PrintBackground = Options.PrintBackground,
        Scale = (decimal)Options.Scale,
        PageRanges = Options.PageRanges ?? string.Empty,
        Outline = Options.GenerateOutline,
        Tagged = true,
        MarginOptions = new MarginOptions
        {
            Top = Margins.Top.ToCssMillimeters(),
            Right = Margins.Right.ToCssMillimeters(),
            Bottom = Margins.Bottom.ToCssMillimeters(),
            Left = Margins.Left.ToCssMillimeters(),
        },
        DisplayHeaderFooter = headerHtml is not null || footerHtml is not null,
        // Chromium prints a default title/URL header when a template is missing; an empty element suppresses it.
        HeaderTemplate = headerHtml ?? "<span></span>",
        FooterTemplate = footerHtml ?? "<span></span>",
    };

    /// <summary>
    /// Wraps rendered header/footer markup in a band of exactly the configured height, adjacent to the body and aligned
    /// with its left and right margins. Overflow is detected separately and fails the conversion.
    /// </summary>
    public string BandHtml(string content, bool header, bool forMeasurement)
    {
        var band = header ? Options.Header! : Options.Footer!;
        var height = band.Height.ToMillimeters();
        var width = WidthMm - Margins.Left.ToMillimeters() - Margins.Right.ToMillimeters();
        var offset = (header ? Margins.Top : Margins.Bottom).ToMillimeters() - height;
        var position = forMeasurement
            ? $"position:relative;width:{Mm(width)};"
            : $"position:fixed;left:{Margins.Left.ToCssMillimeters()};right:{Margins.Right.ToCssMillimeters()};{(header ? "top" : "bottom")}:{Mm(offset)};";
        var id = header ? "h2p-header" : "h2p-footer";
        return $"<div id=\"{id}\" style=\"{position}height:{Mm(height)};box-sizing:border-box;overflow:hidden;margin:0;padding:0;" +
               "font-family:sans-serif;font-size:10pt;line-height:1.2;color:#000;-webkit-print-color-adjust:exact;print-color-adjust:exact;\">" +
               content + "</div>";
    }

    private static string Mm(double value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "mm";

    private static PdfConversionException Invalid(string message) => new(PdfConversionError.InvalidRequest, message);

    [GeneratedRegex(@"^\s*[1-9]\d*(\s*-\s*([1-9]\d*)?)?(\s*,\s*[1-9]\d*(\s*-\s*([1-9]\d*)?)?)*\s*$")]
    private static partial Regex PageRangePattern();
}

internal static class HtmlText
{
    public static string Encode(string value) => WebUtility.HtmlEncode(value);
}
