using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Firecrawl;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Registers everything the supplier-catalog sync needs: the Firecrawl API client (built to the
/// OpenAPI spec), the background worker that runs syncs, and the sync orchestration service.
/// </summary>
public static class FirecrawlServiceCollectionExtensions
{
    public static IServiceCollection AddSupplierCatalogSync(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FirecrawlOptions>(configuration.GetSection(FirecrawlOptions.SectionName));

        var syncOptions = configuration.GetSection("SupplierSync").Get<SupplierSyncOptions>()
                          ?? new SupplierSyncOptions();
        services.AddSingleton(syncOptions);

        // Firecrawl API client - a typed HttpClient over the spec's endpoints.
        services.AddHttpClient<IFirecrawlClient, FirecrawlClient>();

        services.AddSingleton<ISupplierSyncQueue, SupplierSyncQueue>();
        services.AddScoped<ISupplierCatalogSyncService, SupplierCatalogSyncService>();
        services.AddHostedService<SupplierSyncBackgroundService>();

        return services;
    }
}
