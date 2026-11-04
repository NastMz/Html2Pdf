using Nast.Html2Pdf.Security;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class LocalFileGuardTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly TempDirectory _outside = new();
    private readonly LocalFileGuard _guard;

    public LocalFileGuardTests()
    {
        _root.File("docs/index.html", "<p>ok</p>");
        _root.File("assets/logo.svg", Svg.BlueSquare);
        _outside.File("secret.txt", "TOP-SECRET");
        _guard = new LocalFileGuard(new LocalFileOptions { AllowedRoots = { _root.Path } });
    }

    private static Uri Url(string path) => new(LocalOrigins.RootBase(0), path);

    [Fact]
    public void Files_inside_the_root_are_served_with_a_content_type()
    {
        var result = _guard.Read(Url("assets/logo.svg"));
        Assert.Equal(LocalReadStatus.Success, result.Status);
        Assert.Equal("image/svg+xml", result.ContentType);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("docs/../../secret.txt")]
    [InlineData("%2e%2e/secret.txt")]
    [InlineData("docs/%2e%2e%2f%2e%2e%2fsecret.txt")]
    [InlineData("docs/..%2f..%2fsecret.txt")]
    [InlineData("docs/..%5c..%5csecret.txt")]
    [InlineData("docs/%00index.html")]
    public void Traversal_attempts_never_escape_the_root(string path)
    {
        var result = _guard.Read(new Uri(LocalOrigins.RootBase(0).AbsoluteUri + path));
        Assert.NotEqual(LocalReadStatus.Success, result.Status);
    }

    [Fact]
    public void Unknown_roots_and_other_hosts_are_denied()
    {
        Assert.Equal(LocalReadStatus.Denied, _guard.Read(new Uri(LocalOrigins.RootBase(5), "x")).Status);
        Assert.Equal(LocalReadStatus.Denied, _guard.Read(new Uri("http://example.com/x")).Status);
    }

    [Fact]
    public void Symbolic_links_that_leave_the_root_are_denied()
    {
        if (!TryCreateSymlinks())
        {
            Assert.Skip("Symbolic links cannot be created on this system.");
        }

        Assert.Equal(LocalReadStatus.Denied, _guard.Read(Url("link-to-secret.txt")).Status);
        Assert.Equal(LocalReadStatus.Denied, _guard.Read(Url("outside-dir/secret.txt")).Status);
        Assert.Equal(LocalReadStatus.Success, _guard.Read(Url("inside-link.svg")).Status);
        Assert.Null(_guard.ResolveInput(_root.Combine("outside-dir/secret.txt"), false, out var error, out _));
        Assert.Equal(PdfConversionError.AccessDenied, error);
    }

    [Fact]
    public void Roots_reached_through_a_symbolic_link_are_canonicalized()
    {
        var link = _outside.Combine("root-link");
        try
        {
            Directory.CreateSymbolicLink(link, _root.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links cannot be created on this system.");
        }

        var guard = new LocalFileGuard(new LocalFileOptions { AllowedRoots = { link } });
        var file = guard.ResolveInput(Path.Combine(link, "docs", "index.html"), false, out _, out _);
        Assert.NotNull(file);
        Assert.Equal(Path.Combine("docs", "index.html"), file.RelativePath);
    }

    [Fact]
    public void Input_paths_must_be_absolute_and_inside_a_root()
    {
        Assert.Null(_guard.ResolveInput("docs/index.html", false, out var error, out _));
        Assert.Equal(PdfConversionError.InvalidRequest, error);

        Assert.Null(_guard.ResolveInput(Path.Combine(_outside.Path, "secret.txt"), false, out error, out var reason));
        Assert.Equal(PdfConversionError.AccessDenied, error);
        Assert.DoesNotContain(_outside.Path, reason);

        Assert.Null(_guard.ResolveInput(Path.Combine(_outside.Path, "missing.txt"), false, out error, out _));
        Assert.Equal(PdfConversionError.AccessDenied, error); // existence outside the roots is not revealed

        Assert.Null(_guard.ResolveInput(_root.Combine("docs/missing.html"), false, out error, out _));
        Assert.Equal(PdfConversionError.ResourceUnavailable, error);

        Assert.Null(_guard.ResolveInput(_root.Combine("docs/../../" + Path.GetFileName(_outside.Path) + "/secret.txt"), false, out error, out _));
        Assert.Equal(PdfConversionError.AccessDenied, error);
    }

    [Fact]
    public void No_roots_means_no_local_access()
    {
        var guard = new LocalFileGuard(new LocalFileOptions());
        Assert.False(guard.HasRoots);
        Assert.Null(guard.ResolveInput(_root.Combine("docs/index.html"), false, out var error, out _));
        Assert.Equal(PdfConversionError.AccessDenied, error);
    }

    [Fact]
    public void Root_configuration_is_validated()
    {
        Assert.Throws<ArgumentException>(() => new LocalFileGuard(new LocalFileOptions { AllowedRoots = { "relative/path" } }));
        Assert.Throws<ArgumentException>(() => new LocalFileGuard(new LocalFileOptions { AllowedRoots = { _root.Combine("does-not-exist") } }));
        Assert.Throws<ArgumentException>(() => new LocalFileGuard(new LocalFileOptions { AllowedRoots = { _root.Path }, MaxFileBytes = 0 }));
    }

    [Fact]
    public void Oversized_files_are_refused()
    {
        var guard = new LocalFileGuard(new LocalFileOptions { AllowedRoots = { _root.Path }, MaxFileBytes = 10 });
        Assert.Equal(LocalReadStatus.TooLarge, guard.Read(Url("assets/logo.svg")).Status);
    }

    [Fact]
    public void Undeclared_html_encodings_default_to_utf8()
    {
        Assert.Equal("text/html; charset=utf-8", ContentTypes.HtmlContentType("<p>hi</p>"u8));
        Assert.Equal("text/html", ContentTypes.HtmlContentType("<meta charset=\"iso-8859-1\"><p>hi</p>"u8));
        Assert.Equal("text/html", ContentTypes.HtmlContentType([0xEF, 0xBB, 0xBF, (byte)'<']));
    }

    private bool TryCreateSymlinks()
    {
        try
        {
            File.CreateSymbolicLink(_root.Combine("link-to-secret.txt"), Path.Combine(_outside.Path, "secret.txt"));
            Directory.CreateSymbolicLink(_root.Combine("outside-dir"), _outside.Path);
            File.CreateSymbolicLink(_root.Combine("inside-link.svg"), _root.Combine("assets/logo.svg"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _root.Dispose();
        _outside.Dispose();
    }
}
