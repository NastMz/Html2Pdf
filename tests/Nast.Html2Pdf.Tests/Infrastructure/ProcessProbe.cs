using Nast.Html2Pdf.Rendering;

namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>
/// Observes browser processes started with a given profile directory. Process counting covers Linux and macOS
/// (the same enumeration the library uses to reap detached processes); resident memory is Linux only (/proc).
/// </summary>
internal static class ProcessProbe
{
    public static bool Supported => ProfileProcesses.Supported;

    public static bool ResidentMemorySupported => OperatingSystem.IsLinux();

    public static int CountProcessesUsing(string marker) => Supported ? ProfileProcesses.Find(marker).Count : 0;

    public static long ResidentBytesUsing(string marker)
    {
        if (!ResidentMemorySupported)
        {
            return 0;
        }

        long total = 0;
        foreach (var pid in ProfileProcesses.Find(marker))
        {
            try
            {
                var rss = File.ReadLines($"/proc/{pid}/status").FirstOrDefault(l => l.StartsWith("VmRSS:", StringComparison.Ordinal));
                if (rss is not null)
                {
                    total += long.Parse(rss.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
            }
        }

        return total;
    }

    public static async Task<int> WaitForNoProcessesAsync(string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int count;
        while ((count = CountProcessesUsing(marker)) > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        return count;
    }
}
