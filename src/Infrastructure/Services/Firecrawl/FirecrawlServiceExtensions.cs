using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using FirecrawlApi;              // AddFirecrawlApiClient, FirecrawlApiClientOptions
using FirecrawlApi.Servers;      // ServerEnvironment

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Registers the Firecrawl-backed <see cref="ISupplierCatalogScraper"/> and the underlying SDK client.
/// The caller passes a bound <see cref="FirecrawlSettings"/>; no configuration is read here.
/// </summary>
public static class FirecrawlServiceExtensions
{
    public static IServiceCollection AddFirecrawlSupplierScraper(
        this IServiceCollection services,
        FirecrawlSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        // AddFirecrawlApiClient registers FirecrawlApiClient as a singleton whose HttpClient is owned by
        // IHttpClientFactory (it calls AddHttpClient internally).
        services.AddFirecrawlApiClient(options =>
        {
            options.BearerAuth = settings.ApiKey;
            options.Environment = ServerEnvironment.Production;

            // Optional override: when a base URL is supplied, use it verbatim as the API base address.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }
        });

        services.AddScoped<ISupplierCatalogScraper, FirecrawlSupplierCatalogScraper>();

        return services;
    }
}
