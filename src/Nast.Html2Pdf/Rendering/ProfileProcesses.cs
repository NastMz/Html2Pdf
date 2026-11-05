using System.Diagnostics;
using System.Globalization;

namespace Nast.Html2Pdf.Rendering;

/// <summary>
/// Finds processes that belong to a browser instance by its private profile directory, which appears on their command
/// lines (<c>--user-data-dir</c>, or the crash reporter's database inside the profile). Killing the browser's process
/// tree does not reach processes that detached from it: on macOS Chrome's <c>chrome_crashpad_handler</c> is reparented,
/// survives the browser, and keeps the host's standard output open.
/// </summary>
internal static class ProfileProcesses
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Whether process command lines can be inspected on this platform.</summary>
    public static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>Process IDs, other than the current process, whose command line contains <paramref name="marker"/>.</summary>
    public static IReadOnlyList<int> Find(string marker)
    {
        var self = Environment.ProcessId;
        return CommandLines()
            .Where(p => p.Pid != self && p.CommandLine.Contains(marker, StringComparison.Ordinal))
            .Select(p => p.Pid)
            .ToList();
    }

    /// <summary>Terminates every process found by <see cref="Find"/>; returns how many were terminated.</summary>
    public static int KillAll(string marker)
    {
        var killed = 0;
        foreach (var pid in Find(marker))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
                killed++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Already gone, or not ours to signal.
            }
        }

        return killed;
    }

    private static List<(int Pid, string CommandLine)> CommandLines()
    {
        if (OperatingSystem.IsLinux())
        {
            return LinuxCommandLines();
        }

        return OperatingSystem.IsMacOS() ? MacCommandLines() : [];
    }

    private static List<(int Pid, string CommandLine)> LinuxCommandLines()
    {
        var result = new List<(int, string)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            try
            {
                result.Add((pid, File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ')));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The process exited while listing.
            }
        }

        return result;
    }

    private static List<(int Pid, string CommandLine)> MacCommandLines()
    {
        var result = new List<(int, string)>();
        var startInfo = new ProcessStartInfo("/bin/ps")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-axww", "-o", "pid=", "-o", "command=" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var ps = Process.Start(startInfo);
            if (ps is null)
            {
                return result;
            }

            var output = ps.StandardOutput.ReadToEndAsync();
            _ = ps.StandardError.ReadToEndAsync();
            if (!output.Wait(ListTimeout))
            {
                ps.Kill();
                return result;
            }

            foreach (var line in output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimStart();
                var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
                if (space > 0 && int.TryParse(trimmed[..space], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                {
                    result.Add((pid, trimmed[(space + 1)..]));
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Listing is best effort.
        }

        return result;
    }
}
