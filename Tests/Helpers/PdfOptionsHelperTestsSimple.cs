using Nast.Html2Pdf.Helpers;

namespace Nast.Html2Pdf.Tests.Helpers
{
    public class PdfOptionsHelperTestsSimple
    {
        [Fact]
        public void A4Standard_ShouldReturnCorrectConfiguration()
        {
            // Act
            var options = PdfOptionsHelper.A4Standard;

            // Assert
            options.Should().NotBeNull();
            options.Format.Should().Be("A4");
            options.Landscape.Should().BeFalse();
            options.PrintBackground.Should().BeTrue();
            options.Scale.Should().Be(1.0f);
            options.Margins.Should().NotBeNull();
            options.Margins.Top.Should().Be("2cm");
            options.Margins.Bottom.Should().Be("2cm");
            options.Margins.Left.Should().Be("2cm");
            options.Margins.Right.Should().Be("2cm");
        }

        [Fact]
        public void A4Landscape_ShouldReturnCorrectConfiguration()
        {
            // Act
            var options = PdfOptionsHelper.A4Landscape;

            // Assert
            options.Should().NotBeNull();
            options.Format.Should().Be("A4");
            options.Landscape.Should().BeTrue();
            options.PrintBackground.Should().BeTrue();
            options.Scale.Should().Be(1.0f);
            options.Margins.Should().NotBeNull();
            options.Margins.Top.Should().Be("2cm");
            options.Margins.Bottom.Should().Be("2cm");
            options.Margins.Left.Should().Be("2cm");
            options.Margins.Right.Should().Be("2cm");
        }

        [Fact]
        public void LetterStandard_ShouldReturnCorrectConfiguration()
        {
            // Act
            var options = PdfOptionsHelper.LetterStandard;

            // Assert
            options.Should().NotBeNull();
            options.Format.Should().Be("Letter");
            options.Landscape.Should().BeFalse();
            options.PrintBackground.Should().BeTrue();
            options.Scale.Should().Be(1.0f);
        }

        [Fact]
        public void AllPresets_ShouldReturnDifferentInstances()
        {
            // Act
            var a4Standard1 = PdfOptionsHelper.A4Standard;
            var a4Standard2 = PdfOptionsHelper.A4Standard;

            // Assert
            a4Standard1.Should().NotBeSameAs(a4Standard2);
            a4Standard1.Should().BeEquivalentTo(a4Standard2);
        }
    }
}
