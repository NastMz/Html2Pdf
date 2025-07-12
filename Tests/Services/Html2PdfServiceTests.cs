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
            var templatePath = "template.html";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

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
                .Setup(x => x.ConvertFromUrlAsync(url, It.IsAny<PdfOptions>()))
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
    }
}
