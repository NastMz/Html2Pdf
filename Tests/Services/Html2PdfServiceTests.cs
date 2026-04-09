using Nast.Html2Pdf.Exceptions;

namespace Nast.Html2Pdf.Tests.Services
{
    public class Html2PdfServiceTests
    {
        private readonly Mock<IHtmlGenerator> _htmlGeneratorMock;
        private readonly Mock<IPdfConverter> _pdfConverterMock;
        private readonly Mock<ILogger<Html2PdfService>> _loggerMock;
        private readonly Mock<Html2PdfDiagnostics> _diagnosticsMock;
        private readonly Html2PdfService _service;

        public Html2PdfServiceTests()
        {
            _htmlGeneratorMock = new Mock<IHtmlGenerator>();
            _pdfConverterMock = new Mock<IPdfConverter>();
            _loggerMock = new Mock<ILogger<Html2PdfService>>();
            _diagnosticsMock = new Mock<Html2PdfDiagnostics>(Mock.Of<ILogger<Html2PdfDiagnostics>>());
            
            _service = new Html2PdfService(
                _htmlGeneratorMock.Object,
                _pdfConverterMock.Object,
                _loggerMock.Object,
                _diagnosticsMock.Object);
        }

        [Fact]
        public async Task GeneratePdfAsync_WithValidTemplate_ShouldReturnSuccessResult()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _htmlGeneratorMock
                .Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(new HtmlResult { Success = true, Html = expectedHtml });

            _pdfConverterMock
                .Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromSeconds(1)));

            // Act
            var result = await _service.GeneratePdfAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldBe(expectedPdfData);
            result.Size.ShouldBe(expectedPdfData.Length);
            result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            result.ErrorMessage.ShouldBeNull();
            result.Exception.ShouldBeNull();
        }

        [Fact]
        public async Task GeneratePdfAsync_WithHtmlGenerationError_ShouldReturnFailureResult()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var exception = new HtmlGenerationException("Template compilation failed");

            _htmlGeneratorMock
                .Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _service.GeneratePdfAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.Size.ShouldBe(0);
            result.ErrorMessage.ShouldContain("Template compilation failed");
            result.Exception.ShouldBeOfType<HtmlGenerationException>();
        }

        [Fact]
        public async Task GeneratePdfAsync_WithPdfConversionError_ShouldReturnFailureResult()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var exception = new PdfConversionException("PDF conversion failed");

            _htmlGeneratorMock
                .Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(new HtmlResult { Success = true, Html = expectedHtml });

            _pdfConverterMock
                .Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _service.GeneratePdfAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.Size.ShouldBe(0);
            result.ErrorMessage.ShouldContain("PDF conversion failed");
            result.Exception.ShouldBeOfType<PdfConversionException>();
        }

        [Fact]
        public async Task GeneratePdfFromFileAsync_WithValidFile_ShouldReturnSuccessResult()
        {
            // Arrange
            var templatePath = Path.Combine(Path.GetTempPath(), $"template-{Guid.NewGuid():N}.html");
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            await File.WriteAllTextAsync(templatePath, "<h1>Hello @Model.Name</h1>");

            _htmlGeneratorMock
                .Setup(x => x.GenerateFromFileAsync(templatePath, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(new HtmlResult { Success = true, Html = expectedHtml });

            _pdfConverterMock
                .Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromSeconds(1)));

            // Act
            var result = await _service.GeneratePdfFromFileAsync(templatePath, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldBe(expectedPdfData);
            result.Size.ShouldBe(expectedPdfData.Length);

            File.Delete(templatePath);
        }

        [Fact]
        public async Task GeneratePdfFromHtmlAsync_WithValidHtml_ShouldReturnSuccessResult()
        {
            // Arrange
            var html = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _pdfConverterMock
                .Setup(x => x.ConvertAsync(html, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromSeconds(1)));

            // Act
            var result = await _service.GeneratePdfFromHtmlAsync(html);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldBe(expectedPdfData);
            result.Size.ShouldBe(expectedPdfData.Length);
            
            // Verify HTML generator was not called
            _htmlGeneratorMock.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<HtmlGenerationOptions>()), Times.Never);
        }

        [Fact]
        public async Task GeneratePdfFromUrlAsync_WithValidUrl_ShouldReturnSuccessResult()
        {
            // Arrange
            var url = "https://google.com";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _pdfConverterMock
                .Setup(x => x.ConvertFromUrlAsync("https://google.com/", It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromSeconds(1)));

            // Act
            var result = await _service.GeneratePdfFromUrlAsync(url);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldBe(expectedPdfData);
            result.Size.ShouldBe(expectedPdfData.Length);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GeneratePdfAsync_WithInvalidTemplate_ShouldReturnFailureResult(string template)
        {
            // Act
            var result = await _service.GeneratePdfAsync(template);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GeneratePdfFromHtmlAsync_WithInvalidHtml_ShouldReturnFailureResult(string html)
        {
            // Act
            var result = await _service.GeneratePdfFromHtmlAsync(html);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GeneratePdfFromUrlAsync_WithInvalidUrl_ShouldReturnFailureResult(string url)
        {
            // Act
            var result = await _service.GeneratePdfFromUrlAsync(url);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task GeneratePdfFromFileAsync_WithMissingTemplateFile_ShouldFailBeforeInvokingDependencies()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.cshtml");

            var result = await _service.GeneratePdfFromFileAsync(missingPath, new { Name = "World" });

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("templatePath");
            _htmlGeneratorMock.Verify(x => x.GenerateFromFileAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<HtmlGenerationOptions>()), Times.Never);
            _pdfConverterMock.Verify(x => x.ConvertAsync(It.IsAny<string>(), It.IsAny<PdfOptions>()), Times.Never);
        }

        [Fact]
        public async Task GeneratePdfFromUrlAsync_WithUnsupportedScheme_ShouldFailBeforeInvokingConverter()
        {
            var result = await _service.GeneratePdfFromUrlAsync("file:///tmp/input.html");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("supported URL schemes");
            _pdfConverterMock.Verify(x => x.ConvertFromUrlAsync(It.IsAny<string>(), It.IsAny<PdfOptions>()), Times.Never);
        }

        [Fact]
        public async Task GeneratePdfAsync_WhenHtmlGeneratorReturnsNull_ShouldFailAndSkipPdfConversion()
        {
            _htmlGeneratorMock
                .Setup(x => x.GenerateAsync("<h1>Hello</h1>", null, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync((HtmlResult?)null);

            var result = await _service.GeneratePdfAsync("<h1>Hello</h1>");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("HTML generation returned null result");
            _pdfConverterMock.Verify(x => x.ConvertAsync(It.IsAny<string>(), It.IsAny<PdfOptions>()), Times.Never);
        }

        [Fact]
        public async Task GeneratePdfAsync_WhenHtmlGeneratorReturnsFailureResult_ShouldReturnWrappedError()
        {
            _htmlGeneratorMock
                .Setup(x => x.GenerateAsync("<h1>Hello</h1>", null, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(new HtmlResult
                {
                    Success = false,
                    ErrorMessage = "template render failed",
                    Exception = new InvalidOperationException("razor failed")
                });

            var result = await _service.GeneratePdfAsync("<h1>Hello</h1>");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("HTML generation failed: template render failed");
            _pdfConverterMock.Verify(x => x.ConvertAsync(It.IsAny<string>(), It.IsAny<PdfOptions>()), Times.Never);
        }

        [Fact]
        public async Task GeneratePdfFromHtmlAsync_WhenConverterReturnsNull_ShouldReturnFailureResult()
        {
            _pdfConverterMock
                .Setup(x => x.ConvertAsync("<h1>Hello</h1>", It.IsAny<PdfOptions>()))
                .ReturnsAsync((PdfResult?)null);

            var result = await _service.GeneratePdfFromHtmlAsync("<h1>Hello</h1>");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("PDF converter returned null result");
        }

        [Fact]
        public async Task GeneratePdfFromHtmlAsync_WhenConverterReturnsSuccessWithoutData_ShouldReturnFailureResult()
        {
            _pdfConverterMock
                .Setup(x => x.ConvertAsync("<h1>Hello</h1>", It.IsAny<PdfOptions>()))
                .ReturnsAsync(new PdfResult
                {
                    Success = true,
                    Data = null,
                    Duration = TimeSpan.FromMilliseconds(10)
                });

            var result = await _service.GeneratePdfFromHtmlAsync("<h1>Hello</h1>");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("data is null");
        }
    }
}
