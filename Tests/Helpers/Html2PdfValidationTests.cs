using Nast.Html2Pdf.Helpers;

namespace Nast.Html2Pdf.Tests.Helpers
{
    public class Html2PdfValidationTests
    {
        [Fact]
        public void EnsureSupportedUri_ShouldAcceptHttpAndHttpsByDefault()
        {
            var httpUri = Html2PdfValidation.EnsureSupportedUri("http://example.com", "httpUrl");
            var httpsUri = Html2PdfValidation.EnsureSupportedUri("https://example.com", "httpsUrl");

            httpUri.Scheme.ShouldBe(Uri.UriSchemeHttp);
            httpsUri.Scheme.ShouldBe(Uri.UriSchemeHttps);
        }

        [Fact]
        public void EnsureSupportedUri_ShouldRejectUnsupportedSchemes()
        {
            var exception = Should.Throw<ArgumentException>(() =>
                Html2PdfValidation.EnsureSupportedUri("file:///tmp/test.html", "url"));

            exception.ParamName.ShouldBe("url");
            exception.Message.ShouldContain("supported URL schemes");
        }

        [Fact]
        public void ValidatePdfOptions_ShouldAcceptValidRangesAndContent()
        {
            var options = new PdfOptions
            {
                Scale = 1.5f,
                Width = 8.27f,
                Height = 11.69f,
                TimeoutMs = 15000,
                Margins = new PdfMargins
                {
                    Top = "1cm",
                    Bottom = "0.5in",
                    Left = "12mm",
                    Right = "24px"
                },
                Header = new PdfHeaderFooter
                {
                    Template = "<span>Header</span>",
                    Height = "1cm"
                },
                Footer = new PdfHeaderFooter
                {
                    Template = "<span>Footer</span>",
                    Height = "12mm"
                }
            };

            var validated = Html2PdfValidation.ValidatePdfOptions(options);

            validated.ShouldBeSameAs(options);
        }

        [Theory]
        [InlineData(0.09f, "scale")]
        [InlineData(2.01f, "scale")]
        public void ValidatePdfOptions_ShouldRejectInvalidScale(float scale, string expectedParamName)
        {
            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                Html2PdfValidation.ValidatePdfOptions(new PdfOptions { Scale = scale }));

            exception.ParamName.ShouldBe(expectedParamName);
            exception.Message.ShouldContain("between 0.1 and 2");
        }

        [Theory]
        [InlineData(0f, "width")]
        [InlineData(-1f, "width")]
        public void ValidatePdfOptions_ShouldRejectNonPositiveCustomWidth(float width, string expectedParamName)
        {
            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                Html2PdfValidation.ValidatePdfOptions(new PdfOptions { Width = width }));

            exception.ParamName.ShouldBe(expectedParamName);
            exception.Message.ShouldContain("greater than zero");
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("12pt")]
        public void ValidatePdfOptions_ShouldRejectUnsupportedMarginUnits(string margin)
        {
            var exception = Should.Throw<ArgumentException>(() =>
                Html2PdfValidation.ValidatePdfOptions(new PdfOptions
                {
                    Margins = new PdfMargins
                    {
                        Top = margin,
                        Bottom = "1cm",
                        Left = "1cm",
                        Right = "1cm"
                    }
                }));

            exception.ParamName.ShouldBe("top");
            exception.Message.ShouldContain("supported margin unit");
        }

        [Fact]
        public void ValidatePdfOptions_ShouldRejectBlankHeaderTemplate()
        {
            var exception = Should.Throw<ArgumentException>(() =>
                Html2PdfValidation.ValidatePdfOptions(new PdfOptions
                {
                    Header = new PdfHeaderFooter
                    {
                        Template = "   ",
                        Height = "1cm"
                    }
                }));

            exception.ParamName.ShouldBe("header.Template");
            exception.Message.ShouldContain("cannot be empty");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void ValidatePositiveTimeout_ShouldRejectNonPositiveValues(int timeoutMs)
        {
            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                Html2PdfValidation.ValidatePositiveTimeout(timeoutMs, "timeoutMs"));

            exception.ParamName.ShouldBe("timeoutMs");
            exception.Message.ShouldContain("greater than zero");
        }

        [Fact]
        public void EnsureRequiredText_ShouldRejectWhitespaceInput()
        {
            var exception = Should.Throw<ArgumentException>(() =>
                Html2PdfValidation.EnsureRequiredText("   ", "template"));

            exception.ParamName.ShouldBe("template");
            exception.Message.ShouldContain("cannot be null or empty");
        }

        [Fact]
        public void EnsureExistingFilePath_ShouldRejectMissingFiles()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.cshtml");

            var exception = Should.Throw<FileNotFoundException>(() =>
                Html2PdfValidation.EnsureExistingFilePath(missingPath, "templatePath"));

            exception.FileName.ShouldBe(missingPath);
            exception.Message.ShouldContain("templatePath");
        }

        [Fact]
        public void ValidateBrowserPoolOptions_ShouldAcceptValidBounds()
        {
            var options = new BrowserPoolOptions
            {
                MinInstances = 1,
                MaxInstances = 2,
                MaxLifetimeMinutes = 30,
                AcquireTimeoutSeconds = 5
            };

            Html2PdfValidation.ValidateBrowserPoolOptions(options).ShouldBeSameAs(options);
        }
    }
}
