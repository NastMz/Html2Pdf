using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

var exe = Environment.GetEnvironmentVariable("NAST_HTML2PDF_CHROMIUM_PATH") ?? throw new InvalidOperationException("set NAST_HTML2PDF_CHROMIUM_PATH");
string[] library =
[
    "--proxy-server=http://egress-blocked.html2pdf.invalid:9", "--proxy-bypass-list=\"<-loopback>\"", "--host-resolver-rules=\"MAP * ~NOTFOUND\"",
    "--force-webrtc-ip-handling-policy=disable_non_proxied_udp", "--webrtc-ip-handling-policy=disable_non_proxied_udp",
    "--disable-features=Translate,MediaRouter,OptimizationHints,AutofillServerCommunication,DialMediaRouteProvider,InterestFeedContentSuggestions,HttpsUpgrades,AcceptCHFrame",
    "--disable-background-networking", "--disable-component-update", "--disable-domain-reliability", "--disable-client-side-phishing-detection",
    "--disable-breakpad", "--disable-sync", "--no-pings", "--deny-permission-prompts", "--disable-dev-shm-usage", "--font-render-hinting=none",
    "--no-first-run", "--no-default-browser-check",
];

async Task<string> Try(string name, string[] args, bool incognito, bool pipe = false, ILoggerFactory? logs = null)
{
    var dir = Directory.CreateTempSubdirectory("diag-").FullName;
    if (OperatingSystem.IsLinux() && Environment.UserName == "root")
    {
        args = [.. args, "--no-sandbox"];
    }

    var watch = Stopwatch.StartNew();
    IBrowser? browser = null;
    try
    {
        var launch = Task.Run(() => Puppeteer.LaunchAsync(new LaunchOptions { ExecutablePath = exe, Headless = true, Pipe = pipe, Args = args, UserDataDir = dir, Timeout = 40000 }, logs));
        browser = await launch.WaitAsync(TimeSpan.FromSeconds(45));
        var launched = watch.ElapsedMilliseconds;
        IPage page;
        if (incognito)
        {
            var context = await browser.CreateBrowserContextAsync().WaitAsync(TimeSpan.FromSeconds(40));
            page = await context.NewPageAsync().WaitAsync(TimeSpan.FromSeconds(40));
        }
        else
        {
            page = await browser.NewPageAsync().WaitAsync(TimeSpan.FromSeconds(40));
        }

        await page.SetContentAsync("<p>ok</p>").WaitAsync(TimeSpan.FromSeconds(20));
        return $"OK    {name}: launch {launched} ms, page+content {watch.ElapsedMilliseconds - launched} ms";
    }
    catch (Exception ex)
    {
        return $"FAIL  {name}: after {watch.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
    }
    finally
    {
        try { if (browser is not null) { browser.Process?.Kill(true); browser.Dispose(); } } catch { }
    }
}

Console.WriteLine($"Chrome: {exe}");
// Round 2: even Chrome's defaults fail on macOS runners because the renderer dies on creation ("Render process gone").
// Test the usual causes of immediate renderer death, starting from defaults.
foreach (var (name, extra) in new (string, string[])[]
{
    ("defaults", []),
    ("--no-sandbox", ["--no-sandbox"]),
    ("--disable-gpu", ["--disable-gpu"]),
    ("--disable-gpu --disable-software-rasterizer", ["--disable-gpu", "--disable-software-rasterizer"]),
    ("--use-angle=swiftshader", ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"]),
    ("--disable-features=SkiaGraphite,Metal", ["--disable-features=SkiaGraphite,Metal"]),
    ("--no-sandbox --disable-gpu", ["--no-sandbox", "--disable-gpu"]),
    ("library args + --no-sandbox", [.. library, "--no-sandbox"]),
    ("library args + --disable-gpu", [.. library, "--disable-gpu"]),
})
{
    Console.WriteLine(await Try(name, extra, incognito: true));
}

// Chrome's own log for the default case, to capture the renderer's crash reason.
Console.WriteLine("---- Chrome stderr (defaults, --enable-logging=stderr) ----");
var logDir = Directory.CreateTempSubdirectory("diag-log-").FullName;
var psi = new ProcessStartInfo(exe, $"--headless --enable-logging=stderr --v=0 --user-data-dir=\"{logDir}\" --dump-dom about:blank")
{
    RedirectStandardError = true,
    RedirectStandardOutput = true,
};
var stderrLines = new System.Collections.Concurrent.ConcurrentQueue<string>();
using (var chrome = new Process { StartInfo = psi })
{
    // Collect output through events with a hard time limit: Chrome's helper processes can keep the pipe open, so
    // reading the stream to the end could block forever.
    chrome.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrLines.Enqueue(e.Data); };
    chrome.OutputDataReceived += (_, _) => { };
    chrome.Start();
    chrome.BeginErrorReadLine();
    chrome.BeginOutputReadLine();
    var exited = chrome.WaitForExit(60000);
    if (!exited)
    {
        try { chrome.Kill(entireProcessTree: true); } catch { }
    }

    foreach (var line in stderrLines.Where(l => l.Contains("ERROR") || l.Contains("FATAL") || l.Contains("crash", StringComparison.OrdinalIgnoreCase) || l.Contains("sandbox", StringComparison.OrdinalIgnoreCase) || l.Contains("renderer", StringComparison.OrdinalIgnoreCase)).Take(60))
    {
        Console.WriteLine(line.Length > 400 ? line[..400] : line);
    }

    Console.WriteLine($"dump-dom exited: {exited}, exit code: {(exited ? chrome.ExitCode : -1)}, stderr lines: {stderrLines.Count}");
}
