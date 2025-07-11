namespace Nast.Html2Pdf.Tests.Models
{
    public class PdfResultTests
    {
        [Fact]
        public void CreateSuccess_ShouldReturnSuccessfulResult()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var duration = TimeSpan.FromSeconds(2);

            // Act
            var result = PdfResult.CreateSuccess(data, duration);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().BeEquivalentTo(data);
            result.Duration.Should().Be(duration);
            result.Size.Should().Be(data.Length);
            result.ErrorMessage.Should().BeNull();
            result.Exception.Should().BeNull();
        }

        [Fact]
        public void CreateFailure_WithMessage_ShouldReturnFailureResult()
        {
            // Arrange
            var errorMessage = "Something went wrong";
            var duration = TimeSpan.FromSeconds(1);

            // Act
            var result = PdfResult.CreateError(errorMessage, null, duration);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.Duration.Should().Be(duration);
            result.Size.Should().Be(0);
            result.ErrorMessage.Should().Be(errorMessage);
            result.Exception.Should().BeNull();
        }

        [Fact]
        public void CreateFailure_WithException_ShouldReturnFailureResult()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");
            var duration = TimeSpan.FromSeconds(1);

            // Act
            var result = PdfResult.CreateError(exception.Message, exception, duration);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.Duration.Should().Be(duration);
            result.Size.Should().Be(0);
            result.ErrorMessage.Should().Be(exception.Message);
            result.Exception.Should().Be(exception);
        }

        [Fact]
        public void CreateFailure_WithExceptionAndMessage_ShouldReturnFailureResult()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");
            var errorMessage = "Custom error message";
            var duration = TimeSpan.FromSeconds(1);

            // Act
            var result = PdfResult.CreateError(errorMessage, exception, duration);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.Duration.Should().Be(duration);
            result.Size.Should().Be(0);
            result.ErrorMessage.Should().Be(errorMessage);
            result.Exception.Should().Be(exception);
        }

        [Fact]
        public void Size_WithNullData_ShouldReturnZero()
        {
            // Arrange
            var result = new PdfResult { Data = null };

            // Act
            var size = result.Size;

            // Assert
            size.Should().Be(0);
        }

        [Fact]
        public void Size_WithEmptyData_ShouldReturnZero()
        {
            // Arrange
            var result = new PdfResult { Data = Array.Empty<byte>() };

            // Act
            var size = result.Size;

            // Assert
            size.Should().Be(0);
        }

        [Fact]
        public void Size_WithData_ShouldReturnDataLength()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var result = new PdfResult { Data = data };

            // Act
            var size = result.Size;

            // Assert
            size.Should().Be(data.Length);
        }

        [Fact]
        public void DefaultConstructor_ShouldInitializeWithDefaults()
        {
            // Act
            var result = new PdfResult();

            // Assert
            result.Success.Should().BeFalse();
            result.Data.Should().BeNull();
            result.ErrorMessage.Should().BeNull();
            result.Exception.Should().BeNull();
            result.Duration.Should().Be(TimeSpan.Zero);
            result.Size.Should().Be(0);
        }
    }
}
