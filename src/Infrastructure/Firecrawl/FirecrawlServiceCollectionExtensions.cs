using FirecrawlApi;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

public static class FirecrawlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Firecrawl client and the supplier-catalog reader. The API key is bound from
    /// <c>Firecrawl:ApiKey</c> (sourced from the <c>FIRECRAWL_API_KEY</c> env var via config/secrets)
    /// and <c>Firecrawl:BaseUrl</c>, when set, is used verbatim as the API base address.
    /// </summary>
    public static IServiceCollection AddFirecrawlSupplierCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FirecrawlOptions>(configuration.GetSection(FirecrawlOptions.SectionName));

        services.AddFirecrawlApiClient(options =>
        {
            options.BearerAuth = configuration[$"{FirecrawlOptions.SectionName}:ApiKey"];

            var baseUrl = configuration[$"{FirecrawlOptions.SectionName}:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                options.Server.Default.Production.BaseUrl = baseUrl;
            }
        });

        services.AddScoped<ISupplierCatalogReader, FirecrawlSupplierCatalogReader>();

        return services;
    }
}
