using System.Diagnostics;
using Xunit;

namespace Nast.Html2Pdf.Tests
{
    /// <summary>
    /// Global test cleanup to ensure Chrome processes are terminated
    /// </summary>
    public class TestCleanup : IDisposable
    {
        public TestCleanup()
        {
            // Force kill any existing Chrome processes before running tests
            ForceKillChromeProcesses();
        }

        public void Dispose()
        {
            // Force kill Chrome processes after tests complete
            ForceKillChromeProcesses();
        }

        private static void ForceKillChromeProcesses()
        {
            try
            {
                var chromeProcessNames = new[] { "chrome", "chromium", "chromium-browser" };
                foreach (var processName in chromeProcessNames)
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                process.WaitForExit(2000); // Wait up to 2 seconds
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore errors when killing processes
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors in cleanup
            }
        }
    }

    /// <summary>
    /// Collection fixture to ensure cleanup runs once per test assembly
    /// </summary>
    [CollectionDefinition("TestCleanup")]
    public class TestCleanupCollectionDefinition : ICollectionFixture<TestCleanup>
    {
    }
}
