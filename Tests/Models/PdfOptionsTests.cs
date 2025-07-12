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
            options.Format.ShouldBe("A4");
            options.Landscape.ShouldBeFalse();
            options.Margins.ShouldNotBeNull();
            options.Header.ShouldBeNull();
            options.Footer.ShouldBeNull();
            options.PrintBackground.ShouldBeTrue();
            options.Scale.ShouldBe(1.0f);
            options.Width.ShouldBeNull();
            options.Height.ShouldBeNull();
            options.PageRanges.ShouldBeNull();
            options.WaitForImages.ShouldBeTrue();
            options.TimeoutMs.ShouldBe(30000);
        }

        [Fact]
        public void Margins_DefaultConstructor_ShouldInitializeWithDefaults()
        {
            // Act
            var margins = new PdfMargins();

            // Assert
            margins.Top.ShouldBe("1cm");
            margins.Bottom.ShouldBe("1cm");
            margins.Left.ShouldBe("1cm");
            margins.Right.ShouldBe("1cm");
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
            options.Format.ShouldBe("Letter");
            options.Landscape.ShouldBeTrue();
            options.Margins.ShouldBe(customMargins);
            options.PrintBackground.ShouldBeFalse();
            options.Scale.ShouldBe(0.8f);
            options.Width.ShouldBe(8.5f);
            options.Height.ShouldBe(11f);
            options.PageRanges.ShouldBe("1-3");
            options.WaitForImages.ShouldBeFalse();
            options.TimeoutMs.ShouldBe(60000);
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
            options.Format.ShouldBe(format);
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
            options.Scale.ShouldBe(scale);
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
            options.TimeoutMs.ShouldBe(timeout);
        }
    }
}
