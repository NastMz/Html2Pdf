using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nast.Html2Pdf;

// Extension methods live in the DI namespace by convention so they are discoverable without extra usings.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of the converter in a service collection.</summary>
public static class Html2PdfServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Html2PdfConverter"/> as a singleton, also exposed as <see cref="IHtml2PdfConverter"/>.
    /// Options come from <see cref="IOptions{TOptions}"/> (so <c>services.Configure&lt;Html2PdfOptions&gt;(section)</c>
    /// works) plus <paramref name="configure"/>. The container disposes the converter, which shuts the browser down.
    /// </summary>
    public static IServiceCollection AddHtml2Pdf(this IServiceCollection services, Action<Html2PdfOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<Html2PdfOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp => new Html2PdfConverter(
            sp.GetRequiredService<IOptions<Html2PdfOptions>>().Value,
            sp.GetService<ILoggerFactory>()));
        services.TryAddSingleton<IHtml2PdfConverter>(sp => sp.GetRequiredService<Html2PdfConverter>());
        return services;
    }
}
