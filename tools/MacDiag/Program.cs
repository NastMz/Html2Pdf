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
Console.WriteLine(await Try("defaults, default context", [], incognito: false));
Console.WriteLine(await Try("defaults, incognito context", [], incognito: true));
Console.WriteLine(await Try("library args, default context", library, incognito: false));
Console.WriteLine(await Try("library args, incognito context", library, incognito: true));
foreach (var arg in library)
{
    Console.WriteLine(await Try($"library args without {arg.Split('=')[0]}", library.Where(a => a != arg).ToArray(), incognito: true));
}

Console.WriteLine("---- protocol log for: library args, incognito context (filtered) ----");
using var factory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Trace)
    .AddFilter((category, level) => true));
var capture = new StringWriter();
var original = Console.Out;
Console.SetOut(capture);
var verdict = await Try("library args, incognito context (logged)", library, incognito: true, logs: factory);
await Task.Delay(500);
Console.SetOut(original);
Console.WriteLine(verdict);
var captured = capture.ToString().Split('\n');
Console.WriteLine($"captured {captured.Length} log lines");
foreach (var line in captured.Where(l => l.Contains("Network.") || l.Contains("loadingFailed") || l.Contains("Target.") || l.Contains("RECV") || l.Contains("detach", StringComparison.OrdinalIgnoreCase)).Take(80))
{
    Console.WriteLine(line.Length > 600 ? line[..600] : line);
}
