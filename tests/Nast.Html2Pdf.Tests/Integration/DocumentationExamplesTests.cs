using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>Executes the examples shown in README.md and docs/*.md so the documentation stays accurate.</summary>
public sealed class DocumentationExamplesTests(ConverterFixture fixture) : IClassFixture<ConverterFixture>
{
    private Html2PdfConverter Converter => fixture.Converter;

    private static string Text(PdfDocument document)
    {
        using var pdf = new PdfInspector(document.Content);
        return pdf.AllText;
    }

    [Fact]
    public async Task Readme_layout_example()
    {
        var options = new PdfConversionOptions
        {
            PaperSize = PaperSize.Letter,
            Orientation = PageOrientation.Landscape,
            Margins = new PageMargins(top: "25mm", right: "15mm", bottom: "20mm", left: "15mm"),
            Header = new PdfHeaderFooter("<div style=\"font-size:9pt\">{{ Company }}</div>", height: "10mm"),
            Footer = new PdfHeaderFooter("<div style=\"text-align:right\">Page {{ pdf.page_number }} of {{ pdf.total_pages }}</div>", "8mm"),
            HeaderFooterModel = new { Company = "Contoso" },
        };
        var text = Text(await Converter.ConvertAsync(PdfSource.FromHtml("<h1>Hello, PDF</h1>"), options));
        Assert.Contains("Contoso", text);
        Assert.Contains("Page 1 of 1", text);
    }

    [Fact]
    public async Task Templates_guide_examples()
    {
        var invoice = new
        {
            Number = "INV-7",
            Customer = new { Name = "Ada", Street = "1 Engine Way", City = "London" },
            Lines = new[] { new { Description = "Work", Total = 1234.5m } },
            Notes = (string?)null,
            Date = new DateTime(2026, 11, 4),
            Body = new TrustedHtml("<em>trusted</em>"),
        };
        var source = PdfSource.FromTemplate("""
            <h1>Invoice {{ Number }}</h1>
            <p>{{ Customer.Name }}</p>
            <table>
              {% for line in Lines %}
                <tr><td>{{ line.Description }}</td><td>{{ line.Total | format_number: 'N2' }}</td></tr>
              {% endfor %}
            </table>
            {% if Notes %}<p>{{ Notes }}</p>{% endif %}
            <p>Issued {{ Date | format_date: 'yyyy-MM-dd' }}</p>
            <div>{{ Body }}</div>
            {% render 'address', street: Customer.Street, city: Customer.City %}
            """, invoice, new TemplateSourceOptions
        {
            Partials = new Dictionary<string, string> { ["address"] = "<address>{{ street }}<br>{{ city }}</address>" },
        });
        var text = Text(await Converter.ConvertAsync(source));
        Assert.Contains("Invoice INV-7", text);
        Assert.Contains("1,234.50", text);
        Assert.Contains("Issued 2026-11-04", text);
        Assert.Contains("trusted", text);
        Assert.Contains("1 Engine Way", text);
        Assert.Contains("London", text);
    }

    [Fact]
    public async Task Readme_error_handling_example_reports_safe_categories()
    {
        try
        {
            await Converter.ConvertToFileAsync(PdfSource.FromHtml("<img src=\"https://cdn.example.com/x.png\">"), Path.Combine(fixture.RootPath, "out.pdf"));
            Assert.Fail("expected a failure");
        }
        catch (PdfConversionException ex)
        {
            Assert.Equal(PdfConversionError.AccessDenied, ex.Error);
            Assert.False(ex.IsTransient);
            Assert.NotNull(ex.OperationId);
        }

        Assert.False(File.Exists(Path.Combine(fixture.RootPath, "out.pdf")));
    }
}
