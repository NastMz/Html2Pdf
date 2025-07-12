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
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldBe(data);
            result.Duration.ShouldBe(duration);
            result.Size.ShouldBe(data.Length);
            result.ErrorMessage.ShouldBeNull();
            result.Exception.ShouldBeNull();
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
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.Duration.ShouldBe(duration);
            result.Size.ShouldBe(0);
            result.ErrorMessage.ShouldBe(errorMessage);
            result.Exception.ShouldBeNull();
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
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.Duration.ShouldBe(duration);
            result.Size.ShouldBe(0);
            result.ErrorMessage.ShouldBe(exception.Message);
            result.Exception.ShouldBe(exception);
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
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.Duration.ShouldBe(duration);
            result.Size.ShouldBe(0);
            result.ErrorMessage.ShouldBe(errorMessage);
            result.Exception.ShouldBe(exception);
        }

        [Fact]
        public void Size_WithNullData_ShouldReturnZero()
        {
            // Arrange
            var result = new PdfResult { Data = null };

            // Act
            var size = result.Size;

            // Assert
            size.ShouldBe(0);
        }

        [Fact]
        public void Size_WithEmptyData_ShouldReturnZero()
        {
            // Arrange
            var result = new PdfResult { Data = Array.Empty<byte>() };

            // Act
            var size = result.Size;

            // Assert
            size.ShouldBe(0);
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
            size.ShouldBe(data.Length);
        }

        [Fact]
        public void DefaultConstructor_ShouldInitializeWithDefaults()
        {
            // Act
            var result = new PdfResult();

            // Assert
            result.Success.ShouldBeFalse();
            result.Data.ShouldBeNull();
            result.ErrorMessage.ShouldBeNull();
            result.Exception.ShouldBeNull();
            result.Duration.ShouldBe(TimeSpan.Zero);
            result.Size.ShouldBe(0);
        }
    }
}
