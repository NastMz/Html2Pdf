using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>Content fidelity and layout, verified with independent PDF tools.</summary>
public sealed class RenderingTests(ConverterFixture fixture) : IClassFixture<ConverterFixture>
{
    private Html2PdfConverter Converter => fixture.Converter;

    [Fact]
    public async Task Invoice_template_produces_searchable_text_unicode_links_and_layout()
    {
        var result = await Converter.ConvertAsync(PdfSource.FromTemplate(Documents.InvoiceTemplate, Documents.InvoiceModel(),
            new TemplateSourceOptions { BaseDirectory = fixture.RootPath }));

        Assert.Empty(result.Warnings);
        Assert.Matches("^[0-9a-f]{16}$", result.OperationId);
        Assert.True(result.Timings.Total >= result.Timings.Rendering);
        using var pdf = new PdfInspector(result.Content);
        Assert.Equal(1, pdf.PageCount);
        var (width, height) = pdf.PageSizeMm(1);
        Assert.Equal(210, width, 0.5);
        Assert.Equal(297, height, 0.5);

        var text = pdf.Text(1);
        Assert.Contains("Invoice INV-2026-0042", text);
        Assert.Contains("Ada Lovelace", text);
        Assert.Contains("955.00", text);
        Assert.Contains("1,175.00 EUR", text);
        Assert.Contains("Café ☕ consulting — naïve façade", text);
        Assert.Contains("https://billing.example.com/invoices/INV-2026-0042", pdf.LinkUris(1));

        // Table rows keep document order.
        Assert.True(text.IndexOf("Implementation work", StringComparison.Ordinal) < text.IndexOf("Support plan", StringComparison.Ordinal));

        // The body starts at the 10mm default margin and the badge background is printed.
        var heading = pdf.FindWordMm(1, "Invoice")!.Value;
        Assert.InRange(heading.X, 9.5, 14);
        var badge = pdf.FindWordMm(1, "PAID")!.Value;
        Assert.True(Raster.IsColor(pdf.Render(1, 150).At(badge.X - 1.5, badge.Y + 2), "blue"));
    }

    [Fact]
    public async Task Multipage_reports_paginate_in_order_with_repeated_headers_and_page_totals()
    {
        var options = new PdfConversionOptions
        {
            Margins = new PageMargins("25mm", "15mm", "20mm", "15mm"),
            Header = new PdfHeaderFooter("<div style=\"font-size:9pt;border-bottom:1px solid #000\">{{ Company }} Quarterly report</div>", "10mm"),
            Footer = new PdfHeaderFooter("<div style=\"font-size:9pt;text-align:right\">Page {{ pdf.page_number }} of {{ pdf.total_pages }}</div>", "8mm"),
            HeaderFooterModel = new { Company = "Contoso & Sons" },
            GenerateOutline = true,
        };
        var result = await Converter.ConvertAsync(PdfSource.FromHtml(Documents.Report(220)), options);

        using var pdf = new PdfInspector(result.Content);
        Assert.InRange(pdf.PageCount, 4, 30);
        Assert.Contains("Row-0001", pdf.Text(1));
        Assert.Contains("END-OF-REPORT", pdf.Text(pdf.PageCount));
        Assert.Contains("Appendix", pdf.Text(pdf.PageCount));
        Assert.DoesNotContain("Row-", pdf.Text(pdf.PageCount)); // the appendix starts on its own page

        var lastRowSeen = 0;
        for (var page = 1; page <= pdf.PageCount; page++)
        {
            var text = pdf.Text(page);
            Assert.Contains("Contoso & Sons Quarterly report", text); // PdfPig cannot map some punctuation glyphs of Type3 header fonts
            Assert.Contains($"Page {page} of {pdf.PageCount}", text);
            Assert.DoesNotContain("{{", text);
            Assert.DoesNotContain("pageNumber", text);
            foreach (var number in System.Text.RegularExpressions.Regex.Matches(text, @"Row-(\d{4})").Select(m => int.Parse(m.Groups[1].Value)))
            {
                Assert.True(number > lastRowSeen, $"row {number} out of order on page {page}");
                lastRowSeen = number;
            }

            // Header text sits inside the top margin, above the body; footer text below it.
            var header = pdf.FindWordMm(page, "Contoso")!.Value;
            Assert.InRange(header.Y, 15, 25);
            var footer = pdf.FindWordMm(page, "Page")!.Value;
            Assert.InRange(footer.Y, 277, 285);
        }

        Assert.Equal(220, lastRowSeen);
        Assert.True(pdf.HasBookmarks);
    }

    [Fact]
    public async Task Header_and_footer_bands_are_placed_exactly_and_never_overlap_the_body()
    {
        var options = new PdfConversionOptions
        {
            Margins = new PageMargins("30mm", "20mm", "25mm", "20mm"),
            Header = new PdfHeaderFooter("<div style=\"height:100%;background:#ff0000\"></div>", "12mm"),
            Footer = new PdfHeaderFooter("<div style=\"height:100%;background:#00a000\"></div>", "10mm"),
        };
        var body = "<body style=\"margin:0\"><div style=\"height:5000mm;background:#0000ff\"></div></body>";
        var result = await Converter.ConvertAsync(PdfSource.FromHtml(body), options);

        using var pdf = new PdfInspector(result.Content);
        Assert.True(pdf.PageCount > 2);
        foreach (var page in new[] { 1, pdf.PageCount / 2 })
        {
            var raster = pdf.Render(page, 150);
            var red = raster.VerticalRun(100, "red")!.Value;
            var blue = raster.VerticalRun(100, "blue")!.Value;
            var green = raster.VerticalRun(100, "green")!.Value;
            Assert.Equal(18, red.Start, 0.6);   // top margin 30 - band 12
            Assert.Equal(30, red.End, 0.6);
            Assert.Equal(30, blue.Start, 0.6);  // body begins right at the margin
            Assert.Equal(272, blue.End, 0.6);   // 297 - 25
            Assert.Equal(272, green.Start, 0.6);
            Assert.Equal(282, green.End, 0.6);
            Assert.True(Raster.IsColor(raster.At(15, 25), "white")); // band is aligned with the body's left margin
            Assert.True(Raster.IsColor(raster.At(25, 25), "red"));
        }
    }

    [Fact]
    public async Task Job_layout_overrides_document_page_rules_and_document_precedence_honours_them()
    {
        var job = await Converter.ConvertAsync(PdfSource.FromHtml(Documents.Letter), new PdfConversionOptions
        {
            PaperSize = PaperSize.Letter,
            Margins = new PageMargins("1in"),
        });
        using (var pdf = new PdfInspector(job.Content))
        {
            var (w, h) = pdf.PageSizeMm(1);
            Assert.Equal(215.9, w, 0.5);
            Assert.Equal(279.4, h, 0.5);
            var raster = pdf.Render(1, 150);
            Assert.True(Raster.IsColor(raster.At(25.4 + 2, 25.4 + 2), "red"));
            Assert.True(Raster.IsColor(raster.At(20, 20), "white"));
        }

        var document = await Converter.ConvertAsync(PdfSource.FromHtml(Documents.Letter), new PdfConversionOptions
        {
            PaperSize = PaperSize.Letter,
            LayoutPrecedence = PageLayoutPrecedence.Document,
        });
        using (var pdf = new PdfInspector(document.Content))
        {
            var (w, h) = pdf.PageSizeMm(1);
            Assert.Equal(210, w, 0.5); // A5 landscape from the document's @page rule
            Assert.Equal(148, h, 0.5);
            Assert.True(Raster.IsColor(pdf.Render(1, 150).At(2, 2), "red")); // document margin 0
        }
    }

    [Theory]
    [InlineData("A3", PageOrientation.Portrait, 297, 420)]
    [InlineData("A4", PageOrientation.Landscape, 297, 210)]
    [InlineData("Legal", PageOrientation.Portrait, 215.9, 355.6)]
    [InlineData("Custom", PageOrientation.Portrait, 100, 150)]
    public async Task Paper_sizes_and_orientation_are_applied(string paper, PageOrientation orientation, double width, double height)
    {
        var size = paper == "Custom" ? PaperSize.Custom("10cm", "150mm") : PaperSize.FromName(paper);
        var result = await Converter.ConvertAsync(PdfSource.FromHtml("<p>size</p>"), new PdfConversionOptions { PaperSize = size, Orientation = orientation });
        using var pdf = new PdfInspector(result.Content);
        var (w, h) = pdf.PageSizeMm(1);
        Assert.Equal(width, w, 0.5);
        Assert.Equal(height, h, 0.5);
    }

    [Fact]
    public async Task Local_documents_load_relative_and_root_relative_resources_from_disk()
    {
        fixture.Root.File("site/css/site.css", "@font-face { font-family: Probe; src: url('../../assets/DejaVuSans.ttf'); } body { font-family: Probe; } .bg { position: absolute; left: 0; top: 0; width: 40mm; height: 20mm; background: url('/assets/red.png'); }");
        var page = fixture.Root.File("site/pages/page.html", """
            <!DOCTYPE html><html><head><link rel="stylesheet" href="../css/site.css"></head>
            <body><h1>Local document</h1><img src="../../assets/green.jpg" style="width:30mm"><div class="bg"></div>
            <img src="data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxMCIgaGVpZ2h0PSIxMCI+PHJlY3Qgd2lkdGg9IjEwIiBoZWlnaHQ9IjEwIiBmaWxsPSJibHVlIi8+PC9zdmc+"></body></html>
            """);

        var result = await Converter.ConvertAsync(PdfSource.FromFile(page));
        Assert.Empty(result.Warnings);
        using var pdf = new PdfInspector(result.Content);
        Assert.Contains("Local document", pdf.Text(1));
        Assert.Contains(pdf.FontNames, f => f.EndsWith("+DejaVuSans", StringComparison.Ordinal)); // embedded subset of the web font
        Assert.True(pdf.ImageCount(1) >= 1); // the JPEG; the SVG stays vector
        Assert.True(Raster.IsColor(pdf.Render(1, 100).At(20, 15), "red")); // CSS background from a root-relative URL
    }

    [Fact]
    public async Task Html_with_a_base_directory_resolves_relative_resources()
    {
        var result = await Converter.ConvertAsync(PdfSource.FromHtml("<body style=\"margin:0\"><img src=\"red.png\" style=\"width:50mm;height:30mm\"></body>",
            Path.Combine(fixture.RootPath, "assets")));
        using var pdf = new PdfInspector(result.Content);
        Assert.True(Raster.IsColor(pdf.Render(1, 100).At(20, 20), "red"));
    }

    [Fact]
    public async Task Template_files_render_with_partials_and_their_directory_as_base()
    {
        var template = fixture.Root.File("templates/letter.liquid", "<html><body><img src=\"../assets/red.png\">{% render 'signature', name: Sender %}<p>{{ Body }}</p></body></html>");
        var result = await Converter.ConvertAsync(PdfSource.FromTemplateFile(template, new { Sender = "Grace Hopper", Body = "Hello there" },
            new TemplateSourceOptions { Partials = new Dictionary<string, string> { ["signature"] = "<p>Signed: {{ name }}</p>" } }));
        using var pdf = new PdfInspector(result.Content);
        Assert.Contains("Signed: Grace Hopper", pdf.Text(1));
        Assert.Contains("Hello there", pdf.Text(1));
        Assert.Equal(1, pdf.ImageCount(1));
    }

    [Fact]
    public async Task Print_background_scale_and_media_type_are_honoured()
    {
        const string html = "<style>@media screen { .p { display:none } } @media print { .s { display:none } }</style><body style=\"margin:0\"><div style=\"background:#ff0000;width:100mm;height:40mm\"></div><p class=\"p\">PRINT-ONLY</p><p class=\"s\">SCREEN-ONLY</p></body>";
        var withBackground = await Converter.ConvertAsync(PdfSource.FromHtml(html));
        var withoutBackground = await Converter.ConvertAsync(PdfSource.FromHtml(html), new PdfConversionOptions { PrintBackground = false });
        var halfScale = await Converter.ConvertAsync(PdfSource.FromHtml(html), new PdfConversionOptions { Scale = 0.5 });
        var screen = await Converter.ConvertAsync(PdfSource.FromHtml(html), new PdfConversionOptions { MediaType = CssMediaType.Screen });

        using (var pdf = new PdfInspector(withBackground.Content))
        {
            Assert.True(Raster.IsColor(pdf.Render(1).At(80, 30), "red"));
            Assert.Contains("PRINT-ONLY", pdf.Text(1));
            Assert.DoesNotContain("SCREEN-ONLY", pdf.Text(1));
        }

        using (var pdf = new PdfInspector(withoutBackground.Content))
        {
            Assert.True(Raster.IsColor(pdf.Render(1).At(80, 30), "white"));
        }

        using (var pdf = new PdfInspector(halfScale.Content))
        {
            Assert.True(Raster.IsColor(pdf.Render(1).At(40, 20), "red"));
            Assert.True(Raster.IsColor(pdf.Render(1).At(80, 30), "white"));
        }

        using (var pdf = new PdfInspector(screen.Content))
        {
            Assert.Contains("SCREEN-ONLY", pdf.Text(1));
            Assert.DoesNotContain("PRINT-ONLY", pdf.Text(1));
        }
    }

    [Fact]
    public async Task Page_ranges_select_pages_and_out_of_range_selections_fail()
    {
        var html = "<p>ONE</p><p style=\"break-before:page\">TWO</p><p style=\"break-before:page\">THREE</p>";
        var result = await Converter.ConvertAsync(PdfSource.FromHtml(html), new PdfConversionOptions { PageRanges = "2-3" });
        using (var pdf = new PdfInspector(result.Content))
        {
            Assert.Equal(2, pdf.PageCount);
            Assert.Contains("TWO", pdf.Text(1));
            Assert.Contains("THREE", pdf.Text(2));
        }

        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => Converter.ConvertAsync(PdfSource.FromHtml(html), new PdfConversionOptions { PageRanges = "7" }));
        Assert.Equal(PdfConversionError.InvalidRequest, ex.Error);
    }

    [Fact]
    public async Task Lazy_images_are_loaded_before_printing()
    {
        var html = "<div style=\"height:2000mm\"></div><img loading=\"lazy\" src=\"red.png\" style=\"width:40mm\">";
        var result = await Converter.ConvertAsync(PdfSource.FromHtml(html, Path.Combine(fixture.RootPath, "assets")));
        using var pdf = new PdfInspector(result.Content);
        Assert.Equal(1, Enumerable.Range(1, pdf.PageCount).Sum(pdf.ImageCount));
    }

    [Fact]
    public async Task File_and_stream_outputs_receive_the_same_document()
    {
        using var output = new TempDirectory();
        var path = output.Combine("invoice.pdf");
        var file = await Converter.ConvertToFileAsync(PdfSource.FromTemplate(Documents.InvoiceTemplate, Documents.InvoiceModel()), path);
        Assert.Equal(path, file.FilePath);
        Assert.Equal(new FileInfo(path).Length, file.ByteCount);
        using (var pdf = new PdfInspector(await File.ReadAllBytesAsync(path)))
        {
            Assert.Contains("INV-2026-0042", pdf.Text(1));
        }

        using var stream = new MemoryStream();
        var streamed = await Converter.ConvertToStreamAsync(PdfSource.FromHtml("<p>streamed</p>"), stream);
        Assert.Equal(stream.Length, streamed.ByteCount);
        using (var pdf = new PdfInspector(stream.ToArray()))
        {
            Assert.Contains("streamed", pdf.Text(1));
        }
    }

    [Fact]
    public async Task Header_failures_are_explicit()
    {
        var margins = new PageMargins("20mm");
        async Task<PdfConversionError> Fail(PdfHeaderFooter header) => (await Assert.ThrowsAsync<PdfConversionException>(() =>
            Converter.ConvertAsync(PdfSource.FromHtml("<p>x</p>"), new PdfConversionOptions { Margins = margins, Header = header }))).Error;

        Assert.Equal(PdfConversionError.InvalidRequest, await Fail(new PdfHeaderFooter("<div style=\"height:30mm\">too tall</div>", "10mm")));
        Assert.Equal(PdfConversionError.InvalidRequest, await Fail(new PdfHeaderFooter("<img src=\"https://example.com/logo.png\">", "10mm")));
        Assert.Equal(PdfConversionError.InvalidRequest, await Fail(new PdfHeaderFooter("<script>1</script>", "10mm")));
        Assert.Equal(PdfConversionError.TemplateError, await Fail(new PdfHeaderFooter("{{ Missing }}", "10mm")));

        // Embedded images are allowed.
        await Converter.ConvertAsync(PdfSource.FromHtml("<p>x</p>"), new PdfConversionOptions
        {
            Margins = margins,
            Header = new PdfHeaderFooter("<img style=\"height:5mm\" src=\"data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxMCIgaGVpZ2h0PSIxMCIvPg==\">", "10mm"),
        });
    }
}
