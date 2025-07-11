using Nast.Html2Pdf.Exceptions;

namespace Nast.Html2Pdf.Tests.Exceptions
{
    public class Html2PdfExceptionTests
    {
        [Fact]
        public void Constructor_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "Test exception message";

            // Act
            var exception = new Html2PdfException(message);

            // Assert
            exception.Message.Should().Be(message);
            exception.InnerException.Should().BeNull();
        }

        [Fact]
        public void Constructor_WithMessageAndInnerException_ShouldSetBoth()
        {
            // Arrange
            var message = "Test exception message";
            var innerException = new InvalidOperationException("Inner exception");

            // Act
            var exception = new Html2PdfException(message, innerException);

            // Assert
            exception.Message.Should().Be(message);
            exception.InnerException.Should().Be(innerException);
        }

        [Fact]
        public void HtmlGenerationException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "HTML generation failed";

            // Act
            var exception = new HtmlGenerationException(message);

            // Assert
            exception.Should().BeOfType<HtmlGenerationException>();
            exception.Should().BeAssignableTo<Html2PdfException>();
            exception.Message.Should().Be(message);
        }

        [Fact]
        public void PdfConversionException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "PDF conversion failed";

            // Act
            var exception = new PdfConversionException(message);

            // Assert
            exception.Should().BeOfType<PdfConversionException>();
            exception.Should().BeAssignableTo<Html2PdfException>();
            exception.Message.Should().Be(message);
        }

        [Fact]
        public void BrowserPoolException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "Browser pool error";

            // Act
            var exception = new BrowserPoolException(message);

            // Assert
            exception.Should().BeOfType<BrowserPoolException>();
            exception.Should().BeAssignableTo<Html2PdfException>();
            exception.Message.Should().Be(message);
        }

        [Fact]
        public void TemplateException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "Template error";

            // Act
            var exception = new TemplateException(message);

            // Assert
            exception.Should().BeOfType<TemplateException>();
            exception.Should().BeAssignableTo<Html2PdfException>();
            exception.Message.Should().Be(message);
        }

        [Fact]
        public void ResourceException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "Resource error";

            // Act
            var exception = new ResourceException(message);

            // Assert
            exception.Should().BeOfType<ResourceException>();
            exception.Should().BeAssignableTo<Html2PdfException>();
            exception.Message.Should().Be(message);
        }

        [Fact]
        public void AllExceptions_WithInnerException_ShouldSetInnerException()
        {
            // Arrange
            var innerException = new InvalidOperationException("Inner exception");

            // Act & Assert
            var htmlException = new HtmlGenerationException("HTML error", innerException);
            htmlException.InnerException.Should().Be(innerException);

            var pdfException = new PdfConversionException("PDF error", innerException);
            pdfException.InnerException.Should().Be(innerException);

            var browserException = new BrowserPoolException("Browser error", innerException);
            browserException.InnerException.Should().Be(innerException);

            var templateException = new TemplateException("Template error", innerException);
            templateException.InnerException.Should().Be(innerException);

            var resourceException = new ResourceException("Resource error", innerException);
            resourceException.InnerException.Should().Be(innerException);
        }
    }
}
