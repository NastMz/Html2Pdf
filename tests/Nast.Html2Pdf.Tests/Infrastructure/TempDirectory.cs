namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>A uniquely named directory deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = CreateRoot("work");
    }

    public string Path { get; }

    public static string CreateRoot(string kind)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "html2pdf-tests", kind, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        // Canonical form: on macOS the temp directory is behind a symbolic link.
        return Nast.Html2Pdf.Security.LocalFileGuard.ResolveRealPath(path)!;
    }

    public string File(string relative, string content)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string File(string relative, byte[] content)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public string Directory_(string relative)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public string Combine(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
