using System.Globalization;
using Nast.Html2Pdf.Templating;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new(new TemplateEngineOptions());

    private Task<string> Render(string template, object? model, IReadOnlyDictionary<string, string>? partials = null)
        => _renderer.RenderAsync(template, model, partials);

    [Fact]
    public async Task Model_values_are_html_encoded()
    {
        var html = await Render("<p>{{ Name }}</p><a title=\"{{ Name }}\">x</a>", new { Name = "<script>alert(1)</script>\"' onmouseover=\"x" });
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("\" onmouseover", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("{{ Secret }}")]
    [InlineData("{% include 'evil' %}")]
    [InlineData("{% render 'evil' %}")]
    [InlineData("{% for i in (1..9999999) %}x{% endfor %}")]
    [InlineData("{{ 'x' | raw }}")]
    [InlineData("{% raw %}{{ Secret }}{% endraw %}")]
    public async Task Model_values_are_never_executed_as_template_code(string payload)
    {
        var partials = new Dictionary<string, string> { ["evil"] = "PARTIAL-EXECUTED" };
        var html = await Render("<p>{{ Value }}</p>", new { Value = payload, Secret = "SECRET-VALUE" }, partials);
        Assert.DoesNotContain("SECRET-VALUE", html);
        Assert.DoesNotContain("PARTIAL-EXECUTED", html);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(payload).Replace("&#39;", "&#x27;"), html);
    }

    [Fact]
    public async Task Values_inside_trusted_partials_are_still_encoded()
    {
        var partials = new Dictionary<string, string> { ["row"] = "<td>{{ item.Name }}</td>" };
        var html = await Render("{% for item in Items %}{% render 'row', item: item %}{% endfor %}", new { Items = new[] { new { Name = "<b>bold</b>" } } }, partials);
        Assert.Equal("<td>&lt;b&gt;bold&lt;/b&gt;</td>", html);
    }

    [Fact]
    public async Task Trusted_html_is_the_only_unencoded_path()
    {
        var html = await Render("{{ Safe }}|{{ Unsafe }}", new { Safe = new TrustedHtml("<b>ok</b>"), Unsafe = "<b>no</b>" });
        Assert.Equal("<b>ok</b>|&lt;b&gt;no&lt;/b&gt;", html);
    }

    [Fact]
    public async Task The_raw_filter_is_rejected_with_guidance()
    {
        var ex = await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{{ Name | raw }}", new { Name = "x" }));
        Assert.Contains("TrustedHtml", ex.Message);
    }

    [Fact]
    public async Task Unknown_filters_undefined_values_and_syntax_errors_fail()
    {
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{{ Name | nonexistent_filter }}", new { Name = "x" }));
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{{ Missing }}", new { Name = "x" }));
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{{ Customer.Nmae }}", new { Customer = new { Name = "x" } }));
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{% if %}", null));
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{{ Name ", new { Name = "x" }));
    }

    [Fact]
    public async Task Null_values_are_not_undefined()
        => Assert.Equal("[]", await Render("[{{ Name }}]", new { Name = (string?)null }));

    [Fact]
    public async Task Partials_resolve_only_from_the_trusted_dictionary()
    {
        var partials = new Dictionary<string, string> { ["header"] = "<h1>{{ Title }}</h1>" };
        Assert.Equal("<h1>T</h1>", await Render("{% include 'header' %}", new { Title = "T" }, partials));
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{% include 'footer' %}", new { Title = "T" }, partials));
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{% include '../../etc/passwd' %}", new { Title = "T" }, partials));
        // A model value can only select among trusted partials, never supply one.
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{% include Name %}", new { Name = "/etc/passwd" }, partials));
    }

    [Fact]
    public async Task Execution_limits_stop_runaway_templates()
    {
        var limited = new TemplateRenderer(new TemplateEngineOptions { MaxSteps = 1000 });
        await Assert.ThrowsAsync<TemplateRenderException>(() => limited.RenderAsync("{% for i in (1..100000) %}{{ i }}{% endfor %}", null, null));
        var partials = new Dictionary<string, string> { ["loop"] = "{% include 'loop' %}" };
        await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{% include 'loop' %}", null, partials));
    }

    [Fact]
    public async Task Exceptions_from_model_members_do_not_leak_their_message()
    {
        var ex = await Assert.ThrowsAsync<TemplateRenderException>(() => Render("{{ Model.Value }}", new { Model = new Throwing() }));
        Assert.DoesNotContain("SENSITIVE", ex.Message);
    }

    [Fact]
    public async Task Culture_is_applied_to_formatting()
    {
        var renderer = new TemplateRenderer(new TemplateEngineOptions { Culture = CultureInfo.GetCultureInfo("de-DE") });
        Assert.Equal("1.234,50", await renderer.RenderAsync("{{ Amount | format_number: 'N2' }}", new { Amount = 1234.5m }, null));
    }

    [Fact]
    public async Task Dictionaries_and_collections_are_supported_models()
    {
        var model = new Dictionary<string, object> { ["Lines"] = new List<object> { new { Sku = "A" }, new { Sku = "B" } } };
        Assert.Equal("AB", await Render("{% for l in Lines %}{{ l.Sku }}{% endfor %}", model));
    }

    private sealed class Throwing
    {
        public string Value => throw new InvalidOperationException("SENSITIVE detail");
    }
}
