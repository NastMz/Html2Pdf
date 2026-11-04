using Nast.Html2Pdf.Output;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly byte[] Old = "%PDF-old"u8.ToArray();
    private static readonly byte[] New = "%PDF-new-content"u8.ToArray();

    [Fact]
    public async Task Creates_a_new_file()
    {
        var path = _dir.Combine("a.pdf");
        Assert.Equal(path, await AtomicFileWriter.WriteAsync(path, New, overwrite: false, CancellationToken.None));
        Assert.Equal(New, await File.ReadAllBytesAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Existing_files_are_kept_unless_overwrite_is_requested()
    {
        var path = _dir.File("a.pdf", Old);
        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => AtomicFileWriter.WriteAsync(path, New, overwrite: false, CancellationToken.None));
        Assert.Equal(PdfConversionError.OutputFailed, ex.Error);
        Assert.Equal(Old, await File.ReadAllBytesAsync(path));

        await AtomicFileWriter.WriteAsync(path, New, overwrite: true, CancellationToken.None);
        Assert.Equal(New, await File.ReadAllBytesAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task A_file_created_concurrently_before_commit_is_not_clobbered()
    {
        var path = _dir.Combine("race.pdf");
        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => AtomicFileWriter.WriteAsync(path, New, overwrite: false, CancellationToken.None,
            (_, _) => File.WriteAllBytesAsync(path, Old)));
        Assert.Equal(PdfConversionError.OutputFailed, ex.Error);
        Assert.Equal(Old, await File.ReadAllBytesAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Failure_or_cancellation_at_the_commit_point_preserves_the_previous_file()
    {
        var path = _dir.File("a.pdf", Old);
        await Assert.ThrowsAsync<PdfConversionException>(() => AtomicFileWriter.WriteAsync(path, New, overwrite: true, CancellationToken.None,
            (_, _) => throw new IOException("disk full")));
        Assert.Equal(Old, await File.ReadAllBytesAsync(path));

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFileWriter.WriteAsync(path, New, overwrite: true, cts.Token,
            (_, _) =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            }));
        Assert.Equal(Old, await File.ReadAllBytesAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Invalid_destinations_fail_as_output_errors()
    {
        Assert.Equal(PdfConversionError.OutputFailed, (await Assert.ThrowsAsync<PdfConversionException>(
            () => AtomicFileWriter.WriteAsync(Path.Combine(_dir.Path, "missing", "a.pdf"), New, false, CancellationToken.None))).Error);
        Assert.Equal(PdfConversionError.OutputFailed, (await Assert.ThrowsAsync<PdfConversionException>(
            () => AtomicFileWriter.WriteAsync(_dir.Directory_("folder"), New, true, CancellationToken.None))).Error);
    }

    private void AssertNoTemporaryFiles()
        => Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.tmp", SearchOption.AllDirectories));

    public void Dispose() => _dir.Dispose();
}
