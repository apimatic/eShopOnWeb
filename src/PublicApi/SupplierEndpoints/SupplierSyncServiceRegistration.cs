using System.Net.Http;
using FirecrawlApi;
using FirecrawlApi.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Firecrawl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Wires up the supplier-catalog-sync feature: the Firecrawl SDK client (configured from the
/// <c>Firecrawl</c> section), the product reader, the orchestration service, the background queue
/// and its worker.
/// </summary>
public static class SupplierSyncServiceRegistration
{
    public static IServiceCollection AddSupplierCatalogSync(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(FirecrawlOptions.SectionName);
        services.Configure<FirecrawlOptions>(section);
        var firecrawlOptions = section.Get<FirecrawlOptions>() ?? new FirecrawlOptions();

        // Register the Firecrawl SDK client as a singleton over a pooled HttpClient. Built manually
        // (rather than via the SDK's AddFirecrawlApiClient helper) so the base URL and credentials
        // come straight from configuration.
        services.AddHttpClient();
        services.AddSingleton(sp =>
        {
            var options = new FirecrawlApiClientOptions
            {
                BearerAuth = firecrawlOptions.ApiKey,
                Environment = ServerEnvironment.Production
            };

            if (!string.IsNullOrWhiteSpace(firecrawlOptions.BaseUrl))
            {
                // Optional override: used verbatim as the API base address for every Firecrawl call.
                options.Server.Default.Production.BaseUrl = firecrawlOptions.BaseUrl!.Trim();
            }

            options.Logging = options.Logging with { LoggerFactory = sp.GetService<ILoggerFactory>() };

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new FirecrawlApiClient(httpClient, options);
        });

        services.AddScoped<ISupplierProductReader, FirecrawlSupplierProductReader>();
        services.AddScoped<ISupplierCatalogSyncService, SupplierCatalogSyncService>();
        services.AddSingleton<ISupplierSyncQueue, SupplierSyncQueue>();
        services.AddHostedService<SupplierSyncBackgroundService>();

        return services;
    }
}
