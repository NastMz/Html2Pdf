namespace Nast.Html2Pdf.Output;

/// <summary>
/// Writes a file so that readers only ever observe the complete previous content or the complete new content.
/// The data is written and flushed to a temporary file in the destination directory, then moved into place
/// atomically. Without <c>overwrite</c> the move fails if the destination exists, even if it appeared during the write.
/// </summary>
internal static class AtomicFileWriter
{
    // beforeCommit: optional fault-injection hook invoked immediately before the commit point (tests only).
    public static async Task<string> WriteAsync(string path, ReadOnlyMemory<byte> content, bool overwrite, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? beforeCommit = null)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new PdfConversionException(PdfConversionError.OutputFailed, "The output directory does not exist.");
        }

        if (Directory.Exists(fullPath))
        {
            throw new PdfConversionException(PdfConversionError.OutputFailed, "The output path is a directory.");
        }

        if (!overwrite && File.Exists(fullPath))
        {
            throw new PdfConversionException(PdfConversionError.OutputFailed, "The output file already exists and overwriting was not requested.");
        }

        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var committed = false;
        try
        {
            await using (var stream = new FileStream(temporary, new FileStreamOptions
                         {
                             Mode = FileMode.CreateNew,
                             Access = FileAccess.Write,
                             Share = FileShare.None,
                             Options = FileOptions.Asynchronous,
                         }))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // Last cancellation point. After this line the commit is not cancellable.
            cancellationToken.ThrowIfCancellationRequested();
            if (beforeCommit is { } hook)
            {
                await hook(fullPath, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            File.Move(temporary, fullPath, overwrite);
            committed = true;
            return fullPath;
        }
        catch (IOException) when (!overwrite && File.Exists(fullPath))
        {
            throw new PdfConversionException(PdfConversionError.OutputFailed, "The output file already exists and overwriting was not requested.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfConversionException(PdfConversionError.OutputFailed, $"The output file could not be written ({ex.GetType().Name}).");
        }
        finally
        {
            if (!committed)
            {
                TryDelete(temporary);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temporary file has a unique hidden name; leaving it does not affect the destination.
        }
    }
}
