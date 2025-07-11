using Microsoft.Extensions.Configuration;

namespace Nast.Html2Pdf.Tests.Helpers
{
    public static class TestConfiguration
    {
        public static IConfiguration GetConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            return configuration;
        }
    }
}
