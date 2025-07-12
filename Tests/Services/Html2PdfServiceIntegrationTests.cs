using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Tests.Services
{
    public class Html2PdfServiceIntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IHtml2PdfService _html2PdfService;
        private readonly Mock<IHtmlGenerator> _htmlGeneratorMock;
        private readonly Mock<IPdfConverter> _pdfConverterMock;
        private readonly Mock<IBrowserPool> _browserPoolMock;

        public Html2PdfServiceIntegrationTests()
        {
            var services = new ServiceCollection();
            
            // Configurar logging
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            
            // Configurar servicios con mocks
            _htmlGeneratorMock = new Mock<IHtmlGenerator>();
            _pdfConverterMock = new Mock<IPdfConverter>();
            _browserPoolMock = new Mock<IBrowserPool>();
            
            services.AddSingleton(_htmlGeneratorMock.Object);
            services.AddSingleton(_pdfConverterMock.Object);
            services.AddSingleton(_browserPoolMock.Object);
            services.AddScoped<IHtml2PdfService, Html2PdfService>();

            _serviceProvider = services.BuildServiceProvider();
            _html2PdfService = _serviceProvider.GetRequiredService<IHtml2PdfService>();
        }

        [Fact]
        public async Task GeneratePdfAsync_WithValidTemplate_ShouldCallHtmlGeneratorAndPdfConverter()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _htmlGeneratorMock.Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(HtmlResult.CreateSuccess(expectedHtml, TimeSpan.FromMilliseconds(100)));

            _pdfConverterMock.Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromMilliseconds(200)));

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, model);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().BeEquivalentTo(expectedPdfData);
            result.Size.Should().Be(expectedPdfData.Length);
            result.Duration.Should().BeGreaterThan(TimeSpan.Zero);

            // Verificar que se llamaron los métodos correctos
            _htmlGeneratorMock.Verify(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()), Times.Once);
            _pdfConverterMock.Verify(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()), Times.Once);
        }

        [Fact]
        public async Task GeneratePdfAsync_WithHtmlGenerationFailure_ShouldReturnFailureResult()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var errorMessage = "Template compilation failed";

            _htmlGeneratorMock.Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(HtmlResult.CreateError(errorMessage, null, TimeSpan.FromMilliseconds(50)));

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, model);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.ErrorMessage.Should().Contain(errorMessage);

            // Verificar que no se llamó al convertidor de PDF
            _pdfConverterMock.Verify(x => x.ConvertAsync(It.IsAny<string>(), It.IsAny<PdfOptions>()), Times.Never);
        }

        [Fact]
        public async Task GeneratePdfAsync_WithPdfConversionFailure_ShouldReturnFailureResult()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var errorMessage = "PDF conversion failed";

            _htmlGeneratorMock.Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(HtmlResult.CreateSuccess(expectedHtml, TimeSpan.FromMilliseconds(100)));

            _pdfConverterMock.Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateError(errorMessage, null, TimeSpan.FromMilliseconds(150)));

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, model);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.ErrorMessage.Should().Contain(errorMessage);

            _htmlGeneratorMock.Verify(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()), Times.Once);
            _pdfConverterMock.Verify(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()), Times.Once);
        }

        [Fact]
        public async Task GeneratePdfFromFileAsync_WithValidFile_ShouldReturnSuccessResult()
        {
            // Arrange
            var templatePath = "template.html";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _htmlGeneratorMock.Setup(x => x.GenerateFromFileAsync(templatePath, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(HtmlResult.CreateSuccess(expectedHtml, TimeSpan.FromMilliseconds(100)));

            _pdfConverterMock.Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromMilliseconds(200)));

            // Act
            var result = await _html2PdfService.GeneratePdfFromFileAsync(templatePath, model);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().BeEquivalentTo(expectedPdfData);
            result.Size.Should().Be(expectedPdfData.Length);

            _htmlGeneratorMock.Verify(x => x.GenerateFromFileAsync(templatePath, model, It.IsAny<HtmlGenerationOptions>()), Times.Once);
            _pdfConverterMock.Verify(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()), Times.Once);
        }

        [Fact]
        public async Task GeneratePdfFromHtmlAsync_WithValidHtml_ShouldReturnSuccessResult()
        {
            // Arrange
            var html = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _pdfConverterMock.Setup(x => x.ConvertAsync(html, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromMilliseconds(200)));

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().BeEquivalentTo(expectedPdfData);
            result.Size.Should().Be(expectedPdfData.Length);

            // Verificar que no se llamó al generador HTML
            _htmlGeneratorMock.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<HtmlGenerationOptions>()), Times.Never);
            _pdfConverterMock.Verify(x => x.ConvertAsync(html, It.IsAny<PdfOptions>()), Times.Once);
        }

        [Fact]
        public async Task GeneratePdfFromUrlAsync_WithValidUrl_ShouldReturnSuccessResult()
        {
            // Arrange
            var url = "https://example.com";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            _pdfConverterMock.Setup(x => x.ConvertFromUrlAsync(url, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromMilliseconds(300)));

            // Act
            var result = await _html2PdfService.GeneratePdfFromUrlAsync(url);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().BeEquivalentTo(expectedPdfData);
            result.Size.Should().Be(expectedPdfData.Length);

            // Verificar que no se llamó al generador HTML
            _htmlGeneratorMock.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<HtmlGenerationOptions>()), Times.Never);
            _pdfConverterMock.Verify(x => x.ConvertFromUrlAsync(url, It.IsAny<PdfOptions>()), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GeneratePdfAsync_WithInvalidTemplate_ShouldReturnFailureResult(string template)
        {
            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GeneratePdfFromHtmlAsync_WithInvalidHtml_ShouldReturnFailureResult(string html)
        {
            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GeneratePdfFromUrlAsync_WithInvalidUrl_ShouldReturnFailureResult(string url)
        {
            // Act
            var result = await _html2PdfService.GeneratePdfFromUrlAsync(url);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task GeneratePdfAsync_WithCustomOptions_ShouldPassOptionsCorrectly()
        {
            // Arrange
            var template = "<h1>Hello @Model.Name</h1>";
            var model = new { Name = "World" };
            var expectedHtml = "<h1>Hello World</h1>";
            var expectedPdfData = new byte[] { 1, 2, 3, 4, 5 };

            var pdfOptions = new PdfOptions { Format = "A4", Landscape = true };
            var htmlOptions = new HtmlGenerationOptions { InlineStyles = false };

            _htmlGeneratorMock.Setup(x => x.GenerateAsync(template, model, It.IsAny<HtmlGenerationOptions>()))
                .ReturnsAsync(HtmlResult.CreateSuccess(expectedHtml, TimeSpan.FromMilliseconds(100)));

            _pdfConverterMock.Setup(x => x.ConvertAsync(expectedHtml, It.IsAny<PdfOptions>()))
                .ReturnsAsync(PdfResult.CreateSuccess(expectedPdfData, TimeSpan.FromMilliseconds(200)));

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, model, pdfOptions, htmlOptions);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            _htmlGeneratorMock.Verify(x => x.GenerateAsync(template, model, It.Is<HtmlGenerationOptions>(o => o.InlineStyles == false)), Times.Once);
            _pdfConverterMock.Verify(x => x.ConvertAsync(expectedHtml, It.Is<PdfOptions>(o => o.Format == "A4" && o.Landscape)), Times.Once);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
            }
        }
    }
}
