using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>
/// Conversions must not observe each other's browser state, even on the same origin, concurrently, or after failures.
/// The probe document runs with scripts enabled (the most permissive setting) and prints whatever state it finds.
/// </summary>
public sealed class IsolationTests : IAsyncLifetime
{
    private const string Probe = """
        <!DOCTYPE html><html><body><p id="out">pending</p><script>
        (async () => {
          const token = document.documentElement.dataset.token;
          const seen = [];
          if (localStorage.getItem('token')) seen.push('local:' + localStorage.getItem('token'));
          if (sessionStorage.getItem('token')) seen.push('session:' + sessionStorage.getItem('token'));
          if (document.cookie) seen.push('cookie:' + document.cookie);
          const idb = await new Promise(resolve => {
            const open = indexedDB.open('probe', 1);
            open.onupgradeneeded = () => open.result.createObjectStore('kv');
            open.onsuccess = () => {
              const db = open.result;
              const get = db.transaction('kv').objectStore('kv').get('token');
              get.onsuccess = () => {
                const previous = get.result;
                const tx = db.transaction('kv', 'readwrite');
                tx.objectStore('kv').put(token, 'token');
                tx.oncomplete = () => resolve(previous);
              };
            };
            open.onerror = () => resolve('idb-error');
          });
          if (idb) seen.push('idb:' + idb);
          localStorage.setItem('token', token);
          sessionStorage.setItem('token', token);
          document.cookie = 'token=' + token + '; max-age=3600; path=/';
          document.getElementById('out').textContent = 'TOKEN=' + token + ' SEEN=' + (seen.join(',') || 'none');
          window.probeDone = true;
        })();
        </script></body></html>
        """;

    private readonly TempDirectory _root = new();
    private Html2PdfConverter _converter = null!;

    public ValueTask InitializeAsync()
    {
        _converter = TestEnvironment.Create(o =>
        {
            o.AllowScripts = true;
            o.LocalFiles.AllowedRoots.Add(_root.Path);
            o.Capacity.MaxConcurrentConversions = 4;
        });
        return ValueTask.CompletedTask;
    }

    private async Task<string> RunProbeAsync(string token, PdfConversionOptions? options = null)
    {
        // Same directory, therefore the same origin, for every conversion.
        var html = Probe.Replace("<html>", $"<html data-token=\"{token}\">");
        options ??= new PdfConversionOptions();
        options.Readiness.WaitForExpression = "window.probeDone === true";
        var result = await _converter.ConvertAsync(PdfSource.FromHtml(html, _root.Path), options);
        using var pdf = new PdfInspector(result.Content);
        return pdf.AllText;
    }

    [Fact]
    public async Task Sequential_conversions_on_the_same_origin_share_no_storage_cookies_or_databases()
    {
        Assert.Contains("TOKEN=first SEEN=none", await RunProbeAsync("first"));
        Assert.Contains("TOKEN=second SEEN=none", await RunProbeAsync("second"));
    }

    [Fact]
    public async Task Concurrent_conversions_are_isolated_and_receive_only_their_own_output()
    {
        var tokens = Enumerable.Range(0, 12).Select(i => $"tok{i:D2}").ToArray();
        var texts = await Task.WhenAll(tokens.Select(t => RunProbeAsync(t)));
        for (var i = 0; i < tokens.Length; i++)
        {
            Assert.Contains($"TOKEN={tokens[i]} SEEN=none", texts[i]);
            foreach (var other in tokens.Where(t => t != tokens[i]))
            {
                Assert.DoesNotContain(other, texts[i]);
            }
        }
    }

    [Fact]
    public async Task State_from_a_failed_conversion_does_not_survive()
    {
        var html = Probe.Replace("<html>", "<html data-token=\"failed\">");
        var ex = await Assert.ThrowsAsync<PdfConversionException>(() => _converter.ConvertAsync(PdfSource.FromHtml(html, _root.Path), new PdfConversionOptions
        {
            Timeout = TimeSpan.FromSeconds(3),
            Readiness = { WaitForSelector = "#never-appears" },
        }));
        Assert.Equal(PdfConversionError.ReadinessNotMet, ex.Error);
        Assert.Contains("TOKEN=after SEEN=none", await RunProbeAsync("after"));
    }

    public async ValueTask DisposeAsync()
    {
        await _converter.DisposeAsync();
        _root.Dispose();
    }
}
