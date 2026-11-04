using Nast.Html2Pdf.Rendering;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class LayoutTests
{
    [Theory]
    [InlineData("10mm", 10)]
    [InlineData("1.5cm", 15)]
    [InlineData("1in", 25.4)]
    [InlineData("72pt", 25.4)]
    [InlineData("96px", 25.4)]
    [InlineData("0", 0)]
    [InlineData(" 2 MM ", 2)]
    public void Length_parses_supported_units(string text, double expectedMm)
        => Assert.Equal(expectedMm, Length.Parse(text).ToMillimeters(), 6);

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("-1mm")]
    [InlineData("1e3mm")]
    [InlineData("10em")]
    [InlineData("NaNmm")]
    [InlineData("1,5cm")]
    public void Length_rejects_invalid_text(string text) => Assert.Throws<FormatException>(() => Length.Parse(text));

    [Fact]
    public void Length_rejects_negative_and_non_finite_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Length(-1, LengthUnit.Millimeter));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Length(double.PositiveInfinity, LengthUnit.Millimeter));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Length(double.NaN, LengthUnit.Inch));
    }

    [Fact]
    public void Paper_sizes_resolve_by_name_and_validate_custom_dimensions()
    {
        Assert.Same(PaperSize.Letter, PaperSize.FromName("letter"));
        Assert.Throws<ArgumentException>(() => PaperSize.FromName("A9"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaperSize.Custom("5mm", "100mm"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaperSize.Custom("100mm", "6m".Length > 0 ? Length.Millimeters(6000) : Length.Zero));
        Assert.Equal(100, PaperSize.Custom("10cm", "15cm").Width.ToMillimeters(), 6);
    }

    [Fact]
    public void Landscape_swaps_dimensions()
    {
        var geometry = PageGeometry.Create(new PdfConversionOptions { PaperSize = PaperSize.A4, Orientation = PageOrientation.Landscape });
        Assert.Equal(297, geometry.WidthMm, 6);
        Assert.Equal(210, geometry.HeightMm, 6);
    }

    public static TheoryData<string, PdfConversionOptions> ImpossibleLayouts => new()
    {
        { "margins", new PdfConversionOptions { Margins = new PageMargins("110mm", "10mm", "10mm", "10mm") with { Bottom = "180mm" } } },
        { "horizontal margins", new PdfConversionOptions { Margins = new PageMargins("10mm", "105mm", "10mm", "100mm") } },
        { "scale low", new PdfConversionOptions { Scale = 0.05 } },
        { "scale high", new PdfConversionOptions { Scale = 2.5 } },
        { "scale NaN", new PdfConversionOptions { Scale = double.NaN } },
        { "header taller than margin", new PdfConversionOptions { Header = new PdfHeaderFooter("h", "15mm") } },
        { "footer taller than margin", new PdfConversionOptions { Footer = new PdfHeaderFooter("f", "11mm") } },
        { "empty header", new PdfConversionOptions { Margins = new PageMargins("20mm"), Header = new PdfHeaderFooter(" ", "10mm") } },
        { "header with document precedence", new PdfConversionOptions { Margins = new PageMargins("20mm"), LayoutPrecedence = PageLayoutPrecedence.Document, Header = new PdfHeaderFooter("h", "10mm") } },
        { "page ranges", new PdfConversionOptions { PageRanges = "0-2" } },
        { "page ranges text", new PdfConversionOptions { PageRanges = "first" } },
        { "timeout", new PdfConversionOptions { Timeout = TimeSpan.Zero } },
        { "idle", new PdfConversionOptions { Readiness = { NetworkIdleTime = TimeSpan.FromMinutes(10) } } },
        { "selector", new PdfConversionOptions { Readiness = { WaitForSelector = " " } } },
        { "enum", new PdfConversionOptions { Orientation = (PageOrientation)7 } },
        { "null margins", new PdfConversionOptions { Margins = null! } },
    };

    [Theory]
    [MemberData(nameof(ImpossibleLayouts))]
    public void Conflicting_or_impossible_layouts_fail_explicitly(string name, PdfConversionOptions options)
    {
        var ex = Assert.Throws<PdfConversionException>(() => PageGeometry.Create(options));
        Assert.Equal(PdfConversionError.InvalidRequest, ex.Error);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1-3, 5")]
    [InlineData("2-")]
    [InlineData(" 1 - 2 ,4,6-9 ")]
    public void Valid_page_ranges_are_accepted(string ranges)
        => PageGeometry.Create(new PdfConversionOptions { PageRanges = ranges });

    [Fact]
    public void Job_precedence_produces_an_authoritative_page_rule()
    {
        var geometry = PageGeometry.Create(new PdfConversionOptions { PaperSize = PaperSize.Letter, Margins = new PageMargins("1in") });
        Assert.Equal("@page { size: 215.9mm 279.4mm !important; margin: 25.4mm 25.4mm 25.4mm 25.4mm !important; }", geometry.AuthoritativePageCss);
        Assert.Null(PageGeometry.Create(new PdfConversionOptions { LayoutPrecedence = PageLayoutPrecedence.Document }).AuthoritativePageCss);
    }
}
