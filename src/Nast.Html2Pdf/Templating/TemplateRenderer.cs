using System.Text.Encodings.Web;
using Fluid;
using Fluid.Ast;
using Fluid.Values;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Nast.Html2Pdf.Templating;

/// <summary>
/// Renders trusted Liquid templates with untrusted models. Every output of a model value is HTML-encoded, except
/// <see cref="TrustedHtml"/> values; model values are data and are never parsed as template code. The <c>raw</c>
/// filter is removed, unknown filters and undefined values are errors, and execution steps and partial nesting are bounded.
/// </summary>
internal sealed class TemplateRenderer
{
    private static readonly FluidParser Parser = new(new FluidParserOptions { AllowFunctions = false, AllowParentheses = false });
    private readonly TemplateEngineOptions _options;
    private readonly HashSet<string> _knownFilters;

    public TemplateRenderer(TemplateEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.Culture);
        if (options.MaxSteps <= 0 || options.MaxRecursion <= 0)
        {
            throw new ArgumentException("Templates.MaxSteps and Templates.MaxRecursion must be positive.");
        }

        _options = options;
        var probe = CreateOptions(null);
        _knownFilters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in KnownFilterNames(probe))
        {
            _knownFilters.Add(name);
        }
    }

    public async Task<string> RenderAsync(string template, object? model, IReadOnlyDictionary<string, string>? partials, IReadOnlyDictionary<string, object>? extraValues = null)
    {
        var parsed = Parse(template, "template");
        if (partials is not null)
        {
            foreach (var (name, partial) in partials)
            {
                if (string.IsNullOrWhiteSpace(name) || partial is null)
                {
                    throw new TemplateRenderException("Partial names and contents cannot be empty.");
                }

                Parse(partial, $"partial '{name}'");
            }
        }

        var options = CreateOptions(partials);
        var context = model is null ? new TemplateContext(options) : new TemplateContext(model, options, allowModelMembers: true);
        if (extraValues is not null)
        {
            foreach (var (name, value) in extraValues)
            {
                context.SetValue(name, value);
            }
        }

        try
        {
            return await parsed.RenderAsync(context, HtmlEncoder.Default).ConfigureAwait(false);
        }
        catch (TemplateRenderException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw new TemplateRenderException("The template references a partial that was not provided.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase))
        {
            throw new TemplateRenderException("The template exceeded its execution limits (Templates.MaxSteps or Templates.MaxRecursion).");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Exceptions from model members may carry data; report only the type.
            throw new TemplateRenderException($"Template rendering failed ({ex.GetType().Name}).");
        }
    }

    private IFluidTemplate Parse(string template, string what)
    {
        if (!Parser.TryParse(template, out var parsed, out var error))
        {
            throw new TemplateRenderException($"The {what} has a syntax error: {error}");
        }

        if (_knownFilters.Count > 0 && parsed is Fluid.Parser.FluidTemplate fluidTemplate)
        {
            var visitor = new FilterCollector();
            visitor.VisitTemplate(fluidTemplate);
            foreach (var name in visitor.Names)
            {
                if (name == "raw")
                {
                    throw new TemplateRenderException($"The {what} uses the 'raw' filter, which is not supported. Pass markup the application trusts as a TrustedHtml model value instead.");
                }

                if (!_knownFilters.Contains(name))
                {
                    throw new TemplateRenderException($"The {what} uses an unknown filter '{name}'.");
                }
            }
        }

        return parsed;
    }

    private TemplateOptions CreateOptions(IReadOnlyDictionary<string, string>? partials)
    {
        var options = new TemplateOptions
        {
            MemberAccessStrategy = new UnsafeMemberAccessStrategy(),
            CultureInfo = _options.Culture,
            MaxSteps = _options.MaxSteps,
            MaxRecursion = _options.MaxRecursion,
            FileProvider = new PartialsProvider(partials),
            Undefined = name => throw new TemplateRenderException($"The template references an undefined value '{name}'."),
        };
        options.Filters.Remove("raw");
        options.ValueConverters.Add(value => value is TrustedHtml trusted ? new StringValue(trusted.Html, encode: false) : null);
        return options;
    }

    private static IEnumerable<string> KnownFilterNames(TemplateOptions options)
    {
        // FilterCollection exposes its registrations through enumeration in current Fluid versions.
        if (options.Filters is IEnumerable<KeyValuePair<string, FilterDelegate>> pairs)
        {
            return pairs.Select(p => p.Key);
        }

        return [];
    }

    private sealed class FilterCollector : AstVisitor
    {
        public List<string> Names { get; } = [];

        protected override Expression VisitFilterExpression(FilterExpression filterExpression)
        {
            Names.Add(filterExpression.Name);
            return base.VisitFilterExpression(filterExpression);
        }
    }

    /// <summary>Resolves partials only from the trusted dictionary supplied by the application.</summary>
    private sealed class PartialsProvider(IReadOnlyDictionary<string, string>? partials) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath)
        {
            var name = subpath.TrimStart('/', '\\');
            if (name.EndsWith(".liquid", StringComparison.Ordinal) && partials?.ContainsKey(name) != true)
            {
                name = name[..^".liquid".Length];
            }

            return partials is not null && partials.TryGetValue(name, out var content)
                ? new PartialFile(name, content)
                : new NotFoundFileInfo(name);
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class PartialFile(string name, string content) : IFileInfo
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(content);

        public bool Exists => true;
        public long Length => _bytes.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(_bytes, writable: false);
    }
}

internal sealed class TemplateRenderException(string message) : Exception(message);
