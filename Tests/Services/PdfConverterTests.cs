using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Models;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using ModelPdfOptions = Nast.Html2Pdf.Models.PdfOptions;

namespace Nast.Html2Pdf.Tests.Services
{
    public class PdfConverterTests
    {
        private readonly Mock<IBrowserPool> _browserPoolMock = new();
        private readonly Mock<ILogger<PdfConverter>> _loggerMock = new();

        [Fact]
        public async Task ConvertAsync_WithInvalidPdfOptions_ShouldFailBeforeAcquiringBrowserPage()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);

            var result = await converter.ConvertAsync("<html><body>Hello</body></html>", new ModelPdfOptions
            {
                Scale = 3f
            });

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("scale must be between 0.1 and 2");
            _browserPoolMock.Verify(x => x.GetPageAsync(), Times.Never);
        }

        [Fact]
        public async Task ConvertFromUrlAsync_WithUnsupportedScheme_ShouldFailBeforeAcquiringBrowserPage()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);

            var result = await converter.ConvertFromUrlAsync("file:///tmp/input.html");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("supported URL schemes");
            _browserPoolMock.Verify(x => x.GetPageAsync(), Times.Never);
        }

        [Fact]
        public async Task ConvertFromUrlAsync_WithValidUrl_ShouldUseNormalizedAbsoluteUrlAndMappedPdfOptions()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);
            var pageMock = new Mock<IPage>();
            var pooledPageMock = CreatePooledPageMock(pageMock.Object);
            PuppeteerSharp.PdfOptions? capturedPdfOptions = null;
            string? navigatedUrl = null;

            pageMock.SetupGet(x => x.IsClosed).Returns(false);
            pageMock.Setup(x => x.GoToAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>()))
                .Callback<string, NavigationOptions>((url, _) => navigatedUrl = url)
                .ReturnsAsync((IResponse?)null);
            pageMock.Setup(x => x.EmulateMediaTypeAsync(MediaType.Print)).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.SetViewportAsync(It.IsAny<ViewPortOptions>())).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.PdfDataAsync(It.IsAny<PuppeteerSharp.PdfOptions>()))
                .Callback<PuppeteerSharp.PdfOptions>(options => capturedPdfOptions = options)
                .ReturnsAsync([1, 2, 3]);

            _browserPoolMock.Setup(x => x.GetPageAsync()).ReturnsAsync(pooledPageMock.Object);

            var result = await converter.ConvertFromUrlAsync("https://example.com", new ModelPdfOptions
            {
                Format = "Letter",
                Landscape = true,
                PrintBackground = false,
                Scale = 0.9f,
                Width = 8.5f,
                Height = 11f,
                PageRanges = "1-2",
                WaitForImages = false,
                Margins = new PdfMargins
                {
                    Top = "2cm",
                    Bottom = "3cm",
                    Left = "1cm",
                    Right = "1.5cm"
                },
                Header = new PdfHeaderFooter { Template = "<span>Header</span>", Height = "1cm" },
                Footer = new PdfHeaderFooter { Template = "<span>Footer</span>", Height = "1cm" }
            });

            result.Success.ShouldBeTrue();
            navigatedUrl.ShouldBe("https://example.com/");
            capturedPdfOptions.ShouldNotBeNull();
            capturedPdfOptions.Format.ShouldBe(PaperFormat.Letter);
            capturedPdfOptions.Landscape.ShouldBeTrue();
            capturedPdfOptions.PrintBackground.ShouldBeFalse();
            capturedPdfOptions.Scale.ShouldBe(0.9m);
            capturedPdfOptions.Width.ShouldBe("8.5in");
            capturedPdfOptions.Height.ShouldBe("11in");
            capturedPdfOptions.PageRanges.ShouldBe("1-2");
            capturedPdfOptions.DisplayHeaderFooter.ShouldBeTrue();
            capturedPdfOptions.HeaderTemplate.ShouldBe("<span>Header</span>");
            capturedPdfOptions.FooterTemplate.ShouldBe("<span>Footer</span>");
            capturedPdfOptions.MarginOptions.ShouldNotBeNull();
            capturedPdfOptions.MarginOptions.Top.ShouldBe("2cm");
            capturedPdfOptions.MarginOptions.Bottom.ShouldBe("3cm");
        }

        [Fact]
        public async Task ConvertAsync_WhenBrowserPageAcquisitionFailsOnce_ShouldRetryAndSucceed()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);
            var pageMock = new Mock<IPage>();
            var pooledPageMock = CreatePooledPageMock(pageMock.Object);

            pageMock.SetupGet(x => x.IsClosed).Returns(false);
            pageMock.Setup(x => x.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>())).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.EmulateMediaTypeAsync(MediaType.Print)).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.PdfDataAsync(It.IsAny<PuppeteerSharp.PdfOptions>())).ReturnsAsync([9, 9, 9]);
            pageMock.Setup(x => x.EvaluateExpressionAsync<int>(It.IsAny<string>())).ReturnsAsync(0);

            _browserPoolMock.SetupSequence(x => x.GetPageAsync())
                .ThrowsAsync(new InvalidOperationException("Page is already closed"))
                .ReturnsAsync(pooledPageMock.Object);

            var result = await converter.ConvertAsync("<html><body>retry</body></html>");

            result.Success.ShouldBeTrue();
            _browserPoolMock.Verify(x => x.GetPageAsync(), Times.Exactly(2));
        }

        [Fact]
        public async Task ConvertAsync_WhenSetContentThrowsRetryableException_ShouldRetryAndSucceed()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);
            var firstPageMock = new Mock<IPage>();
            var secondPageMock = new Mock<IPage>();
            var firstPooledPageMock = CreatePooledPageMock(firstPageMock.Object);
            var secondPooledPageMock = CreatePooledPageMock(secondPageMock.Object);

            firstPageMock.SetupGet(x => x.IsClosed).Returns(false);
            firstPageMock.Setup(x => x.EmulateMediaTypeAsync(MediaType.Print)).Returns(Task.CompletedTask);
            firstPageMock.Setup(x => x.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>()))
                .ThrowsAsync(new InvalidOperationException("Page was closed during content load"));

            secondPageMock.SetupGet(x => x.IsClosed).Returns(false);
            secondPageMock.Setup(x => x.EmulateMediaTypeAsync(MediaType.Print)).Returns(Task.CompletedTask);
            secondPageMock.Setup(x => x.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>())).Returns(Task.CompletedTask);
            secondPageMock.Setup(x => x.EvaluateExpressionAsync<int>("document.images.length")).ReturnsAsync(0);
            secondPageMock.Setup(x => x.PdfDataAsync(It.IsAny<PuppeteerSharp.PdfOptions>())).ReturnsAsync([4, 5, 6]);

            _browserPoolMock.SetupSequence(x => x.GetPageAsync())
                .ReturnsAsync(firstPooledPageMock.Object)
                .ReturnsAsync(secondPooledPageMock.Object);

            var result = await converter.ConvertAsync("<html><body>retry-content</body></html>");

            result.Success.ShouldBeTrue();
            _browserPoolMock.Verify(x => x.GetPageAsync(), Times.Exactly(2));
        }

        [Fact]
        public async Task ConvertAsync_WhenSetContentThrowsNonRetryableException_ShouldFailWithoutRetry()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);
            var pageMock = new Mock<IPage>();
            var pooledPageMock = CreatePooledPageMock(pageMock.Object);

            pageMock.SetupGet(x => x.IsClosed).Returns(false);
            pageMock.Setup(x => x.EmulateMediaTypeAsync(MediaType.Print)).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>()))
                .ThrowsAsync(new ArgumentException("invalid html"));

            _browserPoolMock.Setup(x => x.GetPageAsync()).ReturnsAsync(pooledPageMock.Object);

            var result = await converter.ConvertAsync("<html><body>broken</body></html>");

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("invalid html");
            _browserPoolMock.Verify(x => x.GetPageAsync(), Times.Once);
        }

        [Fact]
        public async Task ConvertAsync_WithWaitForImagesEnabledAndNoImages_ShouldSkipWaitingAndSucceed()
        {
            var converter = new PdfConverter(_browserPoolMock.Object, _loggerMock.Object);
            var pageMock = new Mock<IPage>();
            var pooledPageMock = CreatePooledPageMock(pageMock.Object);

            pageMock.SetupGet(x => x.IsClosed).Returns(false);
            pageMock.Setup(x => x.EmulateMediaTypeAsync(MediaType.Print)).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>())).Returns(Task.CompletedTask);
            pageMock.Setup(x => x.EvaluateExpressionAsync<int>("document.images.length")).ReturnsAsync(0);
            pageMock.Setup(x => x.PdfDataAsync(It.IsAny<PuppeteerSharp.PdfOptions>())).ReturnsAsync([7, 8, 9]);

            _browserPoolMock.Setup(x => x.GetPageAsync()).ReturnsAsync(pooledPageMock.Object);

            var result = await converter.ConvertAsync(
                "<html><body><img src='logo.png' /></body></html>",
                new ModelPdfOptions { WaitForImages = true });

            result.Success.ShouldBeTrue();
            pageMock.Verify(x => x.EvaluateExpressionAsync<int>("document.images.length"), Times.Once);
        }

        private static Mock<IPooledPage> CreatePooledPageMock(IPage page)
        {
            var pooledPageMock = new Mock<IPooledPage>();
            pooledPageMock.SetupGet(x => x.Page).Returns(page);
            pooledPageMock.Setup(x => x.Dispose());
            return pooledPageMock;
        }
    }
}
