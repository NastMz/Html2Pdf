namespace Nast.Html2Pdf.Tests.Models
{
    public class PdfOptionsTests
    {
        [Fact]
        public void DefaultConstructor_ShouldInitializeWithDefaults()
        {
            // Act
            var options = new PdfOptions();

            // Assert
            options.Format.Should().Be("A4");
            options.Landscape.Should().BeFalse();
            options.Margins.Should().NotBeNull();
            options.Header.Should().BeNull();
            options.Footer.Should().BeNull();
            options.PrintBackground.Should().BeTrue();
            options.Scale.Should().Be(1.0f);
            options.Width.Should().BeNull();
            options.Height.Should().BeNull();
            options.PageRanges.Should().BeNull();
            options.WaitForImages.Should().BeTrue();
            options.TimeoutMs.Should().Be(30000);
        }

        [Fact]
        public void Margins_DefaultConstructor_ShouldInitializeWithDefaults()
        {
            // Act
            var margins = new PdfMargins();

            // Assert
            margins.Top.Should().Be("1cm");
            margins.Bottom.Should().Be("1cm");
            margins.Left.Should().Be("1cm");
            margins.Right.Should().Be("1cm");
        }

        [Fact]
        public void PdfOptions_SetProperties_ShouldUpdateValues()
        {
            // Arrange
            var options = new PdfOptions();
            var customMargins = new PdfMargins
            {
                Top = "2cm",
                Bottom = "2cm",
                Left = "1.5cm",
                Right = "1.5cm"
            };

            // Act
            options.Format = "Letter";
            options.Landscape = true;
            options.Margins = customMargins;
            options.PrintBackground = false;
            options.Scale = 0.8f;
            options.Width = 8.5f;
            options.Height = 11f;
            options.PageRanges = "1-3";
            options.WaitForImages = false;
            options.TimeoutMs = 60000;

            // Assert
            options.Format.Should().Be("Letter");
            options.Landscape.Should().BeTrue();
            options.Margins.Should().Be(customMargins);
            options.PrintBackground.Should().BeFalse();
            options.Scale.Should().Be(0.8f);
            options.Width.Should().Be(8.5f);
            options.Height.Should().Be(11f);
            options.PageRanges.Should().Be("1-3");
            options.WaitForImages.Should().BeFalse();
            options.TimeoutMs.Should().Be(60000);
        }

        [Theory]
        [InlineData("A4")]
        [InlineData("A3")]
        [InlineData("Letter")]
        [InlineData("Legal")]
        public void Format_ValidValues_ShouldBeSet(string format)
        {
            // Arrange
            var options = new PdfOptions();

            // Act
            options.Format = format;

            // Assert
            options.Format.Should().Be(format);
        }

        [Theory]
        [InlineData(0.1f)]
        [InlineData(1.0f)]
        [InlineData(2.0f)]
        public void Scale_ValidValues_ShouldBeSet(float scale)
        {
            // Arrange
            var options = new PdfOptions();

            // Act
            options.Scale = scale;

            // Assert
            options.Scale.Should().Be(scale);
        }

        [Theory]
        [InlineData(1000)]
        [InlineData(30000)]
        [InlineData(60000)]
        public void TimeoutMs_ValidValues_ShouldBeSet(int timeout)
        {
            // Arrange
            var options = new PdfOptions();

            // Act
            options.TimeoutMs = timeout;

            // Assert
            options.TimeoutMs.Should().Be(timeout);
        }
    }
}
