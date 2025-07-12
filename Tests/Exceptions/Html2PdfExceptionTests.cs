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
            exception.Message.ShouldBe(message);
            exception.InnerException.ShouldBeNull();
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
            exception.Message.ShouldBe(message);
            exception.InnerException.ShouldBe(innerException);
        }

        [Fact]
        public void HtmlGenerationException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "HTML generation failed";

            // Act
            var exception = new HtmlGenerationException(message);

            // Assert
            exception.ShouldBeOfType<HtmlGenerationException>();
            exception.ShouldBeAssignableTo<Html2PdfException>();
            exception.Message.ShouldBe(message);
        }

        [Fact]
        public void PdfConversionException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "PDF conversion failed";

            // Act
            var exception = new PdfConversionException(message);

            // Assert
            exception.ShouldBeOfType<PdfConversionException>();
            exception.ShouldBeAssignableTo<Html2PdfException>();
            exception.Message.ShouldBe(message);
        }

        [Fact]
        public void BrowserPoolException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "Browser pool error";

            // Act
            var exception = new BrowserPoolException(message);

            // Assert
            exception.ShouldBeOfType<BrowserPoolException>();
            exception.ShouldBeAssignableTo<Html2PdfException>();
            exception.Message.ShouldBe(message);
        }

        [Fact]
        public void TemplateException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "Template error";

            // Act
            var exception = new TemplateException(message);

            // Assert
            exception.ShouldBeOfType<TemplateException>();
            exception.ShouldBeAssignableTo<Html2PdfException>();
            exception.Message.ShouldBe(message);
        }

        [Fact]
        public void ResourceException_ShouldInheritFromHtml2PdfException()
        {
            // Arrange
            var message = "Resource error";

            // Act
            var exception = new ResourceException(message);

            // Assert
            exception.ShouldBeOfType<ResourceException>();
            exception.ShouldBeAssignableTo<Html2PdfException>();
            exception.Message.ShouldBe(message);
        }

        [Fact]
        public void AllExceptions_WithInnerException_ShouldSetInnerException()
        {
            // Arrange
            var innerException = new InvalidOperationException("Inner exception");

            // Act & Assert
            var htmlException = new HtmlGenerationException("HTML error", innerException);
            htmlException.InnerException.ShouldBe(innerException);

            var pdfException = new PdfConversionException("PDF error", innerException);
            pdfException.InnerException.ShouldBe(innerException);

            var browserException = new BrowserPoolException("Browser error", innerException);
            browserException.InnerException.ShouldBe(innerException);

            var templateException = new TemplateException("Template error", innerException);
            templateException.InnerException.ShouldBe(innerException);

            var resourceException = new ResourceException("Resource error", innerException);
            resourceException.InnerException.ShouldBe(innerException);
        }
    }
}
