using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nast.Html2Pdf.Security;

internal enum LocalReadStatus
{
    Success,
    NotFound,
    Denied,
    TooLarge,
}

internal sealed record LocalFile(int RootIndex, string FullPath, string RelativePath)
{
    /// <summary>Virtual URL under which the file is served to Chromium.</summary>
    public Uri ToUrl() => new(LocalOrigins.RootBase(RootIndex), string.Join('/', RelativePath.Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)));

    public Uri DirectoryUrl()
    {
        var url = ToUrl().AbsoluteUri;
        return new Uri(url[..(url.LastIndexOf('/') + 1)]);
    }
}

internal sealed record LocalReadResult(LocalReadStatus Status, byte[]? Content = null, string? ContentType = null);

/// <summary>
/// Confines local file access to the configured roots. Paths are canonicalized by resolving every symbolic link and
/// junction, compared against canonical roots on a path-segment boundary, and, after the file is opened, the path of
/// the open handle is verified again so that a link swapped in between check and use is detected.
/// </summary>
/// <remarks>
/// Hard links and bind mounts cannot be distinguished from regular files by any path-based check; roots must not
/// contain hard links or mounts that expose content the application does not intend to publish.
/// </remarks>
internal sealed class LocalFileGuard
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly string[] _roots;
    private readonly long _maxFileBytes;

    public LocalFileGuard(LocalFileOptions options)
    {
        if (options.MaxFileBytes <= 0)
        {
            throw new ArgumentException("LocalFiles.MaxFileBytes must be positive.");
        }

        _maxFileBytes = options.MaxFileBytes;
        _roots = options.AllowedRoots.Select(CanonicalizeRoot).ToArray();
    }

    public bool HasRoots => _roots.Length > 0;

    private static string CanonicalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("Allowed roots must be absolute paths.");
        }

        var resolved = ResolveRealPath(root);
        if (resolved is null || !Directory.Exists(resolved))
        {
            throw new ArgumentException("Every allowed root must be an existing directory.");
        }

        return Path.TrimEndingDirectorySeparator(resolved);
    }

    /// <summary>Resolves an application-supplied absolute path to a file or directory inside a root.</summary>
    /// <returns>The resolved path, or <see langword="null"/> with a reason.</returns>
    public LocalFile? ResolveInput(string path, bool expectDirectory, out PdfConversionError? error, out string? reason)
    {
        error = null;
        reason = null;
        if (_roots.Length == 0)
        {
            error = PdfConversionError.AccessDenied;
            reason = "Local file access is disabled: no allowed roots are configured.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('\0'))
        {
            error = PdfConversionError.InvalidRequest;
            reason = "Local paths must be absolute.";
            return null;
        }

        var resolved = ResolveRealPath(path);
        if (resolved is null && IsLexicallyInsideRoot(path))
        {
            error = PdfConversionError.ResourceUnavailable;
            reason = expectDirectory ? "The base directory does not exist." : "The file does not exist.";
            return null;
        }

        // Paths outside the roots are denied without revealing whether they exist.
        var located = resolved is null ? null : Locate(resolved);
        if (located is null)
        {
            error = PdfConversionError.AccessDenied;
            reason = "The path is outside the allowed local roots.";
            return null;
        }

        var exists = expectDirectory ? Directory.Exists(located.FullPath) : File.Exists(located.FullPath);
        if (!exists)
        {
            error = PdfConversionError.ResourceUnavailable;
            reason = expectDirectory ? "The base directory does not exist." : "The file does not exist.";
            return null;
        }

        return located;
    }

    /// <summary>Reads the file addressed by a virtual local URL.</summary>
    public LocalReadResult Read(Uri url)
    {
        if (!LocalOrigins.TryGetRootIndex(url, out var rootIndex) || rootIndex >= _roots.Length)
        {
            return new LocalReadResult(LocalReadStatus.Denied);
        }

        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>(segments.Length);
        foreach (var raw in segments)
        {
            string segment;
            try
            {
                segment = Uri.UnescapeDataString(raw);
            }
            catch (UriFormatException)
            {
                return new LocalReadResult(LocalReadStatus.Denied);
            }

            if (!IsSafeSegment(segment))
            {
                return new LocalReadResult(LocalReadStatus.Denied);
            }

            parts.Add(segment);
        }

        if (parts.Count == 0)
        {
            return new LocalReadResult(LocalReadStatus.NotFound);
        }

        var candidate = Path.Combine([_roots[rootIndex], .. parts]);
        return ReadFile(candidate, rootIndex);
    }

    /// <summary>Reads an input file resolved by <see cref="ResolveInput"/>.</summary>
    public LocalReadResult Read(LocalFile file) => ReadFile(file.FullPath, file.RootIndex);

    private LocalReadResult ReadFile(string candidate, int rootIndex)
    {
        var resolved = ResolveRealPath(candidate);
        if (resolved is null)
        {
            return new LocalReadResult(LocalReadStatus.NotFound);
        }

        if (!IsInside(resolved, _roots[rootIndex]))
        {
            return new LocalReadResult(LocalReadStatus.Denied);
        }

        if (!File.Exists(resolved))
        {
            return new LocalReadResult(LocalReadStatus.NotFound);
        }

        try
        {
            using var handle = File.OpenHandle(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
            var handlePath = GetHandlePath(handle);
            if (handlePath is not null && !IsInside(handlePath, _roots[rootIndex]))
            {
                return new LocalReadResult(LocalReadStatus.Denied);
            }

            var length = RandomAccess.GetLength(handle);
            if (length > _maxFileBytes)
            {
                return new LocalReadResult(LocalReadStatus.TooLarge);
            }

            var content = new byte[length];
            var offset = 0;
            while (offset < content.Length)
            {
                var read = RandomAccess.Read(handle, content.AsSpan(offset), offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != content.Length)
            {
                Array.Resize(ref content, offset);
            }

            return new LocalReadResult(LocalReadStatus.Success, content, ContentTypes.ForFile(resolved, content));
        }
        catch (FileNotFoundException)
        {
            return new LocalReadResult(LocalReadStatus.NotFound);
        }
        catch (DirectoryNotFoundException)
        {
            return new LocalReadResult(LocalReadStatus.NotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return new LocalReadResult(LocalReadStatus.Denied);
        }
        catch (IOException)
        {
            return new LocalReadResult(LocalReadStatus.NotFound);
        }
    }

    private LocalFile? Locate(string resolved)
    {
        var best = -1;
        for (var i = 0; i < _roots.Length; i++)
        {
            if (IsInside(resolved, _roots[i]) && (best < 0 || _roots[i].Length > _roots[best].Length))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var relative = resolved.Length == _roots[best].Length ? string.Empty : resolved[(_roots[best].Length + 1)..];
        return new LocalFile(best, resolved, relative);
    }

    private bool IsLexicallyInsideRoot(string path)
    {
        var full = Path.GetFullPath(path);
        return _roots.Any(r => IsInside(full, r));
    }

    private static bool IsInside(string path, string root)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        if (path.Equals(root, PathComparison))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, PathComparison);
    }

    private static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.Contains('/') || segment.Contains('\\') || segment.Contains('\0'))
        {
            return false;
        }

        if (IsWindows)
        {
            if (segment.Contains(':') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            var stem = segment.Split('.')[0].TrimEnd(' ');
            if (WindowsDeviceNames.Contains(stem) || segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the absolute path with every symbolic link and junction resolved, or <see langword="null"/> when a
    /// component does not exist or links form a loop.
    /// </summary>
    internal static string? ResolveRealPath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var root = Path.GetPathRoot(full)!;
        var pending = new Stack<string>(Enumerable.Reverse(full[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)));
        var current = root;
        var hops = 0;
        while (pending.Count > 0)
        {
            var part = pending.Pop();
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? current;
                continue;
            }

            var next = Path.Combine(current, part);
            FileSystemInfo info = new FileInfo(next);
            if (!info.Exists)
            {
                info = new DirectoryInfo(next);
                if (!info.Exists && info.LinkTarget is null)
                {
                    return null;
                }
            }

            string? target;
            try
            {
                target = info.LinkTarget;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            if (target is null)
            {
                current = next;
                continue;
            }

            if (++hops > 40)
            {
                return null;
            }

            if (IsWindows && target.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                target = target[4..];
            }

            var absolute = Path.IsPathRooted(target) ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(current, target));
            var targetRoot = Path.GetPathRoot(absolute)!;
            foreach (var segment in Enumerable.Reverse(absolute[targetRoot.Length..]
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)))
            {
                pending.Push(segment);
            }

            current = targetRoot;
        }

        return current;
    }

    private static string? GetHandlePath(SafeFileHandle handle)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var link = new FileInfo($"/proc/self/fd/{handle.DangerousGetHandle()}").LinkTarget;
                return link;
            }

            if (OperatingSystem.IsMacOS())
            {
                var buffer = new byte[1024];
                return NativeMethods.fcntl(handle, NativeMethods.F_GETPATH, buffer) == -1
                    ? null
                    : Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var n and >= 0 ? n : buffer.Length);
            }

            if (IsWindows)
            {
                var builder = new char[32768];
                var length = NativeMethods.GetFinalPathNameByHandleW(handle, builder, (uint)builder.Length, 0);
                if (length == 0 || length >= builder.Length)
                {
                    return null;
                }

                var value = new string(builder, 0, (int)length);
                if (value.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                {
                    return @"\\" + value[8..];
                }

                return value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }

        return null;
    }

    private static class NativeMethods
    {
        public const int F_GETPATH = 50;

        [DllImport("libc", SetLastError = true)]
        public static extern int fcntl(SafeFileHandle fd, int cmd, byte[] buffer);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);
    }
}
