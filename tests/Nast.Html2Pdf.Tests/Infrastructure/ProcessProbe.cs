namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>Counts browser processes started with a given profile directory (Linux only, via /proc).</summary>
internal static class ProcessProbe
{
    public static bool Supported => OperatingSystem.IsLinux();

    public static int CountProcessesUsing(string marker)
    {
        if (!Supported)
        {
            return 0;
        }

        var count = 0;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out _))
            {
                continue;
            }

            try
            {
                var commandLine = File.ReadAllText(Path.Combine(directory, "cmdline"));
                if (commandLine.Contains(marker, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return count;
    }

    public static long ResidentBytesUsing(string marker)
    {
        if (!Supported)
        {
            return 0;
        }

        long total = 0;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out _))
            {
                continue;
            }

            try
            {
                if (!File.ReadAllText(Path.Combine(directory, "cmdline")).Contains(marker, StringComparison.Ordinal))
                {
                    continue;
                }

                var rss = File.ReadLines(Path.Combine(directory, "status")).FirstOrDefault(l => l.StartsWith("VmRSS:", StringComparison.Ordinal));
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
