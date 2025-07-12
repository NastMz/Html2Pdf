using Nast.Html2Pdf.Helpers;

namespace Nast.Html2Pdf.Tests.Helpers
{
    public class PdfOptionsHelperTests
    {
        [Fact]
        public void A4Standard_ShouldReturnCorrectConfiguration()
        {
            // Act
            var options = PdfOptionsHelper.A4Standard;

            // Assert
            options.ShouldNotBeNull();
            options.Format.ShouldBe("A4");
            options.Landscape.ShouldBeFalse();
            options.PrintBackground.ShouldBeTrue();
            options.Scale.ShouldBe(1.0f);
            options.Margins.ShouldNotBeNull();
            options.Margins.Top.ShouldBe("2cm");
            options.Margins.Bottom.ShouldBe("2cm");
            options.Margins.Left.ShouldBe("2cm");
            options.Margins.Right.ShouldBe("2cm");
        }

        [Fact]
        public void A4Landscape_ShouldReturnCorrectConfiguration()
        {
            // Act
            var options = PdfOptionsHelper.A4Landscape;

            // Assert
            options.ShouldNotBeNull();
            options.Format.ShouldBe("A4");
            options.Landscape.ShouldBeTrue();
            options.PrintBackground.ShouldBeTrue();
            options.Scale.ShouldBe(1.0f);
            options.Margins.ShouldNotBeNull();
            options.Margins.Top.ShouldBe("2cm");
            options.Margins.Bottom.ShouldBe("2cm");
            options.Margins.Left.ShouldBe("2cm");
            options.Margins.Right.ShouldBe("2cm");
        }

        [Fact]
        public void LetterStandard_ShouldReturnCorrectConfiguration()
        {
            // Act
            var options = PdfOptionsHelper.LetterStandard;

            // Assert
            options.ShouldNotBeNull();
            options.Format.ShouldBe("Letter");
            options.Landscape.ShouldBeFalse();
            options.PrintBackground.ShouldBeTrue();
            options.Scale.ShouldBe(1.0f);
        }
    }
}
