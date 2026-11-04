using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>A converter with an assets root, shared by the tests of one class.</summary>
public sealed class ConverterFixture : IAsyncLifetime
{
    internal TempDirectory Root { get; } = new();

    public Html2PdfConverter Converter { get; private set; } = null!;

    public string RootPath => Root.Path;

    public ValueTask InitializeAsync()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        foreach (var file in Directory.EnumerateFiles(assets))
        {
            File.Copy(file, Root.Combine("assets/" + Path.GetFileName(file)));
        }

        Converter = TestEnvironment.Create(o => o.LocalFiles.AllowedRoots.Add(Root.Path));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Converter.DisposeAsync();
        Root.Dispose();
    }
}
