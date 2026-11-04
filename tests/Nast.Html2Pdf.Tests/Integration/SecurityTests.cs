using System.Net;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Tests.Infrastructure;

namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>
/// Adversarial tests of the trust boundaries. Every network test is backed by a loopback server that counts requests,
/// so a denial is only accepted if the server was never contacted.
/// </summary>
public sealed class SecurityTests : IDisposable
{
    private readonly TestServer _server = new();
    private readonly TempDirectory _root = new();
    private readonly TempDirectory _outside = new();

    public SecurityTests()
    {
        _root.File("docs/page.html", "<p>PAGE</p>");
        _root.File("assets/red.png", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "red.png")));
        _outside.File("secret.txt", "TOP-SECRET-CONTENT");
    }

    private Html2PdfConverter Create(Action<Html2PdfOptions>? configure = null) => TestEnvironment.Create(o =>
    {
        o.LocalFiles.AllowedRoots.Add(_root.Path);
        configure?.Invoke(o);
    });

    private static async Task<PdfConversionException> Fails(Task task) => await Assert.ThrowsAsync<PdfConversionException>(() => task);

    private static string Text(PdfDocument document)
    {
        using var pdf = new PdfInspector(document.Content);
        return pdf.AllText;
    }

    private void AllowLoopbackServer(Html2PdfOptions o)
    {
        o.Network.AllowedHosts.Add("127.0.0.1");
        o.Network.AllowHttp = true;
        o.Network.AllowedPorts.Add(_server.Port);
        o.Network.AllowedPrivateNetworks.Add("127.0.0.1/32");
    }

    [Fact]
    public async Task Document_scripts_do_not_run_by_default_including_in_frames_and_event_handlers()
    {
        await using var converter = Create();
        var html = """
            <p id="out">STATIC</p>
            <script>document.getElementById('out').textContent = 'SCRIPT-RAN';</script>
            <img src="data:," onerror="document.body.append('HANDLER-RAN')">
            <iframe srcdoc="<p id=f>FRAME</p><script>document.getElementById('f').textContent='FRAME-SCRIPT-RAN'</script>"></iframe>
            <a href="javascript:alert(1)">link</a>
            """;
        var text = Text(await converter.ConvertAsync(PdfSource.FromHtml(html)));
        Assert.Contains("STATIC", text);
        Assert.DoesNotContain("SCRIPT-RAN", text);
        Assert.DoesNotContain("HANDLER-RAN", text);
    }

    [Fact]
    public async Task Scripts_run_only_when_the_application_allows_them()
    {
        await using var converter = Create(o => o.AllowScripts = true);
        var text = Text(await converter.ConvertAsync(PdfSource.FromHtml("<p id=out>STATIC</p><script>document.getElementById('out').textContent='SCRIPT-RAN'</script>"),
            new PdfConversionOptions { Readiness = { WaitForExpression = "document.getElementById('out').textContent === 'SCRIPT-RAN'" } }));
        Assert.Contains("SCRIPT-RAN", text);
    }

    [Fact]
    public async Task With_network_disabled_no_vector_reaches_the_network_even_with_scripts_enabled()
    {
        using var canary = new ConnectionCanary();
        await using var converter = Create(o => o.AllowScripts = true);
        var http = _server.BaseUrl;
        var raw = $"127.0.0.1:{canary.Port}";
        var html = $$"""
            <link rel="stylesheet" href="{{http}}/style.css">
            <link rel="prefetch" href="{{http}}/prefetch">
            <link rel="preconnect" href="http://{{raw}}">
            <link rel="dns-prefetch" href="//canary.example">
            <img src="{{http}}/img.png"><img src="http://localhost:{{_server.Port}}/img2.png">
            <iframe src="{{http}}/frame"></iframe>
            <object data="{{http}}/object"></object>
            <video src="{{http}}/video.mp4" autoplay></video>
            <form id="f" action="{{http}}/form" method="post"><input name="a" value="b"></form>
            <script>
              fetch('{{http}}/fetch').catch(() => {});
              fetch('http://[::ffff:127.0.0.1]:{{_server.Port}}/v6').catch(() => {});
              const x = new XMLHttpRequest(); x.open('GET', '{{http}}/xhr'); x.send();
              navigator.sendBeacon('{{http}}/beacon', 'data');
              try { new WebSocket('ws://{{raw}}/ws'); } catch (e) {}
              try { new EventSource('{{http}}/events'); } catch (e) {}
              try { new Worker('{{http}}/worker.js'); } catch (e) {}
              try { navigator.serviceWorker.register('/sw.js').catch(() => {}); } catch (e) {}
              try { const pc = new RTCPeerConnection({ iceServers: [{ urls: 'stun:{{raw}}' }] }); pc.createDataChannel('d'); pc.createOffer().then(o => pc.setLocalDescription(o)); } catch (e) {}
              try { window.open('{{http}}/popup'); } catch (e) {}
              setTimeout(() => document.getElementById('f').submit(), 10);
              setTimeout(() => { location.href = '{{http}}/navigate'; }, 20);
            </script>
            <p>DONE</p>
            """;
        var result = await converter.ConvertAsync(PdfSource.FromHtml(html), new PdfConversionOptions
        {
            ResourceFailures = ResourceFailurePolicy.BestEffort,
            Readiness = { NetworkIdleTime = TimeSpan.FromMilliseconds(800) },
        });

        await Task.Delay(1000);
        Assert.Equal(0, _server.Hits);
        Assert.Equal(0, canary.Connections);
        Assert.Contains(result.Warnings, w => w.Code == ConversionWarningCode.ResourceDenied);
        Assert.Contains("DONE", Text(result));
    }

    [Fact]
    public async Task Denied_resources_fail_the_conversion_by_default()
    {
        await using var converter = Create();
        var ex = await Fails(converter.ConvertAsync(PdfSource.FromHtml($"<img src=\"{_server.BaseUrl}/img.png?token=SIGNED-SECRET\">")));
        Assert.Equal(PdfConversionError.AccessDenied, ex.Error);
        Assert.DoesNotContain("SIGNED-SECRET", ex.Message);
        Assert.Equal(0, _server.Hits);
    }

    [Fact]
    public async Task Allowed_hosts_are_still_refused_when_they_resolve_to_non_public_addresses()
    {
        IPAddress[] resolved;
        try
        {
            resolved = Dns.GetHostAddresses("private.html2pdf.test");
        }
        catch (System.Net.Sockets.SocketException)
        {
            resolved = [];
        }

        Assert.True(resolved.Contains(IPAddress.Parse("10.255.255.1")),
            "Test precondition: /etc/hosts must map private.html2pdf.test to 10.255.255.1 and loopback.html2pdf.test to 127.0.0.1 (see CI setup).");

        await using var converter = Create(o =>
        {
            o.Network.AllowHttp = true;
            o.Network.AllowedPorts.Add(_server.Port);
            foreach (var host in new[] { "127.0.0.1", "localhost", "loopback.html2pdf.test", "private.html2pdf.test" })
            {
                o.Network.AllowedHosts.Add(host);
            }
        });

        foreach (var url in new[]
                 {
                     $"http://127.0.0.1:{_server.Port}/a.png",
                     $"http://localhost:{_server.Port}/b.png",
                     $"http://loopback.html2pdf.test:{_server.Port}/c.png",
                     $"http://private.html2pdf.test:{_server.Port}/d.png",
                 })
        {
            var ex = await Fails(converter.ConvertAsync(PdfSource.FromHtml($"<img src=\"{url}\">")));
            Assert.Equal(PdfConversionError.AccessDenied, ex.Error);
        }

        Assert.Equal(0, _server.Hits);
    }

    [Fact]
    public async Task Explicitly_allowed_private_networks_are_reachable_with_application_state_stripped()
    {
        _server.MapBytes("/logo.png", "image/png", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "red.png")), headers: new Dictionary<string, string> { ["Set-Cookie"] = "session=abc" });
        await using var converter = Create(AllowLoopbackServer);
        var result = await converter.ConvertAsync(PdfSource.FromHtml($"<img src=\"{_server.BaseUrl}/logo.png\" style=\"width:40mm\"><img src=\"{_server.BaseUrl}/logo.png?again\">"));
        Assert.Empty(result.Warnings);

        var requests = _server.Requests.ToArray();
        Assert.NotEmpty(requests);
        foreach (var request in requests)
        {
            Assert.False(request.Headers.ContainsKey("Cookie"), "cookies must never be sent");
            Assert.False(request.Headers.ContainsKey("Authorization"));
            Assert.False(request.Headers.ContainsKey("Referer"), "the document location must not leak");
        }
    }

    [Fact]
    public async Task Redirects_are_validated_on_every_hop()
    {
        using var other = new TestServer();
        other.MapText("/secret", "text/plain", "INTERNAL");
        _server.MapRedirect("/to-metadata", "http://169.254.169.254/latest/meta-data/");
        _server.MapRedirect("/to-other-port", $"http://127.0.0.1:{other.Port}/secret");
        _server.MapRedirect("/to-unlisted-host", "https://unlisted.example/x.png");
        _server.MapRedirect("/to-file", "file:///etc/passwd");
        _server.MapRedirect("/relative", "/final.png");
        _server.MapBytes("/final.png", "image/png", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "red.png")));
        await using var converter = Create(AllowLoopbackServer);

        foreach (var path in new[] { "/to-metadata", "/to-other-port", "/to-unlisted-host", "/to-file" })
        {
            var ex = await Fails(converter.ConvertAsync(PdfSource.FromHtml($"<img src=\"{_server.BaseUrl}{path}\">")));
            Assert.True(ex.Error is PdfConversionError.AccessDenied or PdfConversionError.ResourceUnavailable, $"{path}: {ex.Error}");
        }

        Assert.Equal(0, other.Hits);
        var ok = await converter.ConvertAsync(PdfSource.FromHtml($"<img src=\"{_server.BaseUrl}/relative\">"));
        Assert.Empty(ok.Warnings);
        Assert.Contains(_server.Requests, r => r.PathAndQuery == "/final.png");
    }

    [Fact]
    public async Task Url_sources_require_explicit_authorization()
    {
        _server.MapText("/doc", "text/html", "<h1>REMOTE-DOC</h1>");
        await using (var closed = Create(o => AllowLoopbackServer(o)))
        {
            Assert.Equal(PdfConversionError.AccessDenied, (await Fails(closed.ConvertAsync(PdfSource.FromUrl($"{_server.BaseUrl}/doc")))).Error);
        }

        Assert.Equal(0, _server.Hits);
        await using var open = Create(o =>
        {
            AllowLoopbackServer(o);
            o.Network.AllowUrlSources = true;
        });
        Assert.Contains("REMOTE-DOC", Text(await open.ConvertAsync(PdfSource.FromUrl($"{_server.BaseUrl}/doc"))));

        foreach (var url in new[] { "file:///etc/passwd", "ftp://127.0.0.1/x", $"http://user:pass@127.0.0.1:{_server.Port}/doc", $"http://127.0.0.1:{_server.Port + 1}/doc", "https://example.com/" })
        {
            Assert.Equal(PdfConversionError.AccessDenied, (await Fails(open.ConvertAsync(PdfSource.FromUrl(url)))).Error);
        }

        _server.MapText("/missing", "text/html", "gone", status: 404);
        Assert.Equal(PdfConversionError.ResourceUnavailable, (await Fails(open.ConvertAsync(PdfSource.FromUrl($"{_server.BaseUrl}/missing")))).Error);
    }

    [Fact]
    public async Task Remote_documents_never_receive_local_file_access()
    {
        _server.MapText("/doc", "text/html", "<h1>REMOTE</h1><iframe src=\"http://r0.files.html2pdf.invalid/docs/page.html\"></iframe><img src=\"http://r0.files.html2pdf.invalid/assets/red.png\">");
        await using var converter = Create(o =>
        {
            AllowLoopbackServer(o);
            o.Network.AllowUrlSources = true;
        });
        var ex = await Fails(converter.ConvertAsync(PdfSource.FromUrl($"{_server.BaseUrl}/doc")));
        Assert.Equal(PdfConversionError.AccessDenied, ex.Error);

        var best = await converter.ConvertAsync(PdfSource.FromUrl($"{_server.BaseUrl}/doc"), new PdfConversionOptions { ResourceFailures = ResourceFailurePolicy.BestEffort });
        Assert.DoesNotContain("PAGE", Text(best));
    }

    [Fact]
    public async Task Html_without_a_base_directory_has_no_local_file_access()
    {
        await using var converter = Create();
        var ex = await Fails(converter.ConvertAsync(PdfSource.FromHtml("<img src=\"http://r0.files.html2pdf.invalid/assets/red.png\">")));
        Assert.Equal(PdfConversionError.AccessDenied, ex.Error);
    }

    [Theory]
    [InlineData("../../{outside}/secret.txt")]
    [InlineData("/../{outside}/secret.txt")]
    [InlineData("%2e%2e/%2e%2e/{outside}/secret.txt")]
    [InlineData("..%2f..%2f{outside}%2fsecret.txt")]
    [InlineData("file://{outsidePath}/secret.txt")]
    [InlineData("http://r1.files.html2pdf.invalid/secret.txt")]
    public async Task Local_documents_cannot_read_files_outside_the_roots(string reference)
    {
        await using var converter = Create();
        var src = reference.Replace("{outside}", Path.GetFileName(_outside.Path)).Replace("{outsidePath}", _outside.Path.Replace('\\', '/'));
        var html = $"<iframe src=\"{src}\" style=\"width:150mm;height:40mm\"></iframe><object data=\"{src}\"></object>";
        var result = await converter.ConvertAsync(PdfSource.FromHtml(html, Path.Combine(_root.Path, "docs")), new PdfConversionOptions { ResourceFailures = ResourceFailurePolicy.BestEffort });
        Assert.DoesNotContain("TOP-SECRET", Text(result));
        // file:// is refused by Chromium itself for http(s) documents, so it may not even produce a request.
        Assert.True(result.Warnings.Count > 0 || reference.StartsWith("file:", StringComparison.Ordinal));
        Assert.All(result.Warnings, w => Assert.DoesNotContain(_outside.Path, w.Resource ?? string.Empty));
    }

    [Fact]
    public async Task Symbolic_links_inside_a_root_cannot_expose_outside_files()
    {
        try
        {
            File.CreateSymbolicLink(_root.Combine("docs/leak.txt"), Path.Combine(_outside.Path, "secret.txt"));
            Directory.CreateSymbolicLink(_root.Combine("docs/leakdir"), _outside.Path);
        }
        catch (Exception linkError) when (linkError is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links cannot be created on this system.");
        }

        await using var converter = Create();
        var html = "<iframe src=\"leak.txt\"></iframe><iframe src=\"leakdir/secret.txt\"></iframe>";
        var ex = await Fails(converter.ConvertAsync(PdfSource.FromHtml(html, Path.Combine(_root.Path, "docs"))));
        Assert.Equal(PdfConversionError.AccessDenied, ex.Error);
        Assert.Equal(PdfConversionError.AccessDenied, (await Fails(converter.ConvertAsync(PdfSource.FromFile(_root.Combine("docs/leak.txt"))))).Error);
        var best = await converter.ConvertAsync(PdfSource.FromHtml(html, Path.Combine(_root.Path, "docs")), new PdfConversionOptions { ResourceFailures = ResourceFailurePolicy.BestEffort });
        Assert.DoesNotContain("TOP-SECRET", Text(best));
    }

    [Fact]
    public async Task Inputs_outside_the_roots_are_denied_without_revealing_paths()
    {
        await using var converter = Create();
        var secret = Path.Combine(_outside.Path, "secret.txt");
        foreach (var source in new[]
                 {
                     PdfSource.FromFile(secret),
                     PdfSource.FromTemplateFile(secret, null),
                     PdfSource.FromHtml("<p>x</p>", _outside.Path),
                 })
        {
            var ex = await Fails(converter.ConvertAsync(source));
            Assert.Equal(PdfConversionError.AccessDenied, ex.Error);
            Assert.DoesNotContain(_outside.Path, ex.Message);
        }

        Assert.Equal(PdfConversionError.InvalidRequest, (await Fails(converter.ConvertAsync(PdfSource.FromFile("docs/page.html")))).Error);
    }

    [Fact]
    public async Task Untrusted_model_values_never_execute_in_documents_headers_or_footers()
    {
        await using var converter = Create(o => o.AllowScripts = true);
        var payloads = new[]
        {
            "{{ Secret }}", "{% include 'p' %}", "<script>document.body.append('INJECTED-SCRIPT')</script>",
            "</p><img src=x onerror=\"document.body.append('INJECTED-HANDLER')\">", "<span class=\"pageNumber\"></span>",
        };
        var model = new { Values = payloads, Secret = "SECRET-MODEL-VALUE", First = payloads[0] };
        var options = new PdfConversionOptions
        {
            Margins = new PageMargins("20mm"),
            Header = new PdfHeaderFooter("<div>H:{{ First }}</div>", "10mm"),
            Footer = new PdfHeaderFooter("<div>F:{{ Values[4] }}</div>", "10mm"),
        };
        var source = PdfSource.FromTemplate("{% for v in Values %}<p>{{ v }}</p>{% endfor %}", model,
            new TemplateSourceOptions { Partials = new Dictionary<string, string> { ["p"] = "PARTIAL-EXECUTED" } });
        var text = Text(await converter.ConvertAsync(source, options));

        Assert.DoesNotContain("SECRET-MODEL-VALUE", text);
        Assert.DoesNotContain("PARTIAL-EXECUTED", text);
        Assert.DoesNotContain("INJECTED-SCRIPT", text.Replace("append('INJECTED-SCRIPT')", string.Empty));
        Assert.DoesNotContain("INJECTED-HANDLER", text.Replace("append('INJECTED-HANDLER')", string.Empty));
        Assert.Contains("{{ Secret }}", text);
        Assert.Matches(@"H\W*\{*\s*Secret", text); // header shows the payload as text (PdfPig drops some header punctuation)
        Assert.Contains("<span", text); // the page-number markup is shown as text, not interpreted
    }

    [Fact]
    public async Task Errors_and_logs_do_not_leak_content_models_secrets_or_paths()
    {
        var logs = new CapturingLoggerProvider(Microsoft.Extensions.Logging.LogLevel.Debug);
        using var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
        await using var converter = TestEnvironment.Create(o => o.LocalFiles.AllowedRoots.Add(_root.Path), factory);
        var failures = new List<PdfConversionException>
        {
            await Fails(converter.ConvertAsync(PdfSource.FromHtml($"<p>HTML-SECRET</p><img src=\"https://cdn.example.com/private/path/img.png?X-Amz-Signature=SIG-SECRET\">"))),
            await Fails(converter.ConvertAsync(PdfSource.FromTemplate("{{ Missing }}", new { Password = "MODEL-SECRET" }))),
            await Fails(converter.ConvertAsync(PdfSource.FromFile(Path.Combine(_outside.Path, "secret.txt")))),
            await Fails(converter.ConvertAsync(PdfSource.FromHtml("<img src=\"missing-file.png\">", _root.Combine("docs")))),
        };

        var everything = string.Join("\n", failures.Select(f => f.Message + f.ToString()).Concat(logs.Entries.Select(e => e.Message)));
        foreach (var secret in new[] { "HTML-SECRET", "SIG-SECRET", "MODEL-SECRET", "TOP-SECRET", _outside.Path, _root.Path, "X-Amz-Signature" })
        {
            Assert.DoesNotContain(secret, everything);
        }

        Assert.All(failures, f => Assert.NotNull(f.OperationId));
        Assert.Contains("https://cdn.example.com/…/img.png", everything);
    }

    [Fact]
    public async Task Navigation_away_from_the_document_is_blocked_or_fails_never_printing_other_content()
    {
        await using var converter = Create(o => o.AllowScripts = true);
        var options = new PdfConversionOptions { Readiness = { NetworkIdleTime = TimeSpan.FromMilliseconds(300) } };

        // Navigations that need a request are refused; the original document is printed.
        var blocked = "<meta http-equiv=\"refresh\" content=\"0;url=http://r0.files.html2pdf.invalid/docs/page.html\"><p>ORIGINAL</p>" +
                      "<script>setTimeout(() => location.reload(), 0)</script>";
        var text = Text(await converter.ConvertAsync(PdfSource.FromHtml(blocked, _root.Combine("docs")), options));
        Assert.Contains("ORIGINAL", text);
        Assert.DoesNotContain("PAGE", text);

        // Navigations that never reach the network cannot be intercepted; the conversion fails instead of printing them.
        var replaced = "<p>ORIGINAL</p><script>location.href = 'about:blank'</script>";
        var ex = await Fails(converter.ConvertAsync(PdfSource.FromHtml(replaced), options));
        Assert.Equal(PdfConversionError.RenderingFailed, ex.Error);
    }

    public void Dispose()
    {
        _server.Dispose();
        _root.Dispose();
        _outside.Dispose();
    }
}
