using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Tests.Services
{
    [Collection("Integration")]
    public class HtmlGeneratorTests
    {
        private const string SampleTemplate = "<h1>Hello @Model.Name</h1>";
        private const string SampleHtml = "<h1>Hello World</h1>";
        private const string WorldName = "World";
        
        private readonly Mock<ILogger<HtmlGenerator>> _loggerMock;
        private readonly HtmlGenerator _htmlGenerator;

        public HtmlGeneratorTests()
        {
            _loggerMock = new Mock<ILogger<HtmlGenerator>>();
            _htmlGenerator = new HtmlGenerator(_loggerMock.Object);
        }

        [Fact]
        public async Task GenerateAsync_WithSimpleTemplate_ShouldReturnSuccess()
        {
            // Arrange
            var template = "Hello {{Name}}!";
            var model = new { Name = WorldName };

            // Act
            var result = await _htmlGenerator.GenerateAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Html.ShouldNotBeNullOrEmpty();
            result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            result.ErrorMessage.ShouldBeNull();
            result.Exception.ShouldBeNull();
        }

        [Fact]
        public async Task GenerateAsync_WithNullTemplate_ShouldReturnError()
        {
            // Act
            var result = await _htmlGenerator.GenerateAsync(null!, null);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Html.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task GenerateAsync_WithEmptyTemplate_ShouldReturnError()
        {
            // Act
            var result = await _htmlGenerator.GenerateAsync("", null);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Html.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task GenerateAsync_WithComplexModel_ShouldProcessCorrectly()
        {
            // Arrange
            var template = "<h1>@Model.Title</h1><p>Count: @Model.Count</p>";
            var model = new
            {
                Title = "Test Title",
                Count = 42
            };

            // Act
            var result = await _htmlGenerator.GenerateAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Html.ShouldNotBeNullOrEmpty();
            result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public async Task GenerateAsync_WithHtmlOptions_ShouldApplyOptions()
        {
            // Arrange
            var template = "<h1>Test</h1>";
            var options = new HtmlGenerationOptions
            {
                InlineStyles = false,
                Encoding = "UTF-8",
                AdditionalCss = "body { margin: 0; }"
            };

            // Act
            var result = await _htmlGenerator.GenerateAsync(template, null, options);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Html.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task GenerateFromFileAsync_WithNonExistentFile_ShouldReturnError()
        {
            // Arrange
            var filePath = "non-existent-file.html";

            // Act
            var result = await _htmlGenerator.GenerateFromFileAsync(filePath, null);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Html.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task GenerateFromResourceAsync_WithInvalidResource_ShouldReturnError()
        {
            // Arrange
            var resourceKey = "invalid-resource";

            // Act
            var result = await _htmlGenerator.GenerateFromResourceAsync(resourceKey, null);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Html.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task GenerateAsync_WithInvalidRazorSyntax_ShouldReturnError()
        {
            // Arrange
            var template = "@Model.NonExistent.Property.That.Does.Not.Exist";
            var model = new { Name = WorldName };

            // Act
            var result = await _htmlGenerator.GenerateAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.Html.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
            result.Exception.ShouldNotBeNull();
        }

        [Theory]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public async Task GenerateAsync_WithWhitespaceTemplate_ShouldReturnSuccess(string template)
        {
            // Act
            var result = await _htmlGenerator.GenerateAsync(template, null);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Html.ShouldNotBeNull(); // Whitespace templates now generate empty/minimal HTML
        }
    }
}
