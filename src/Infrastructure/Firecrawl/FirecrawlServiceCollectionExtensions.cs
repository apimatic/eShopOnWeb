using System;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

public static class FirecrawlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the supplier catalog sync stack: the Firecrawl-backed listing reader (a typed
    /// <see cref="System.Net.Http.HttpClient"/> built against firecrawl-spec) and the domain
    /// import service. Configuration is bound from the <c>Firecrawl:</c> section.
    /// </summary>
    public static IServiceCollection AddSupplierCatalogSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FirecrawlOptions>(configuration.GetSection(FirecrawlOptions.SectionName));

        services.AddHttpClient<IFirecrawlClient, FirecrawlClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<FirecrawlOptions>>().Value;

            // Ensure a trailing slash so relative request paths ("crawl", "crawl/{id}") combine
            // correctly and the spec's "/v2" base segment is preserved.
            var baseUrl = options.ResolvedBaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                // Auth scheme from the spec: bearerAuth (HTTP bearer).
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Max(30, options.PollIntervalSeconds + 120));
        });

        services.AddScoped<ISupplierListingReader, FirecrawlSupplierListingReader>();
        services.AddScoped<ISupplierCatalogSyncService, SupplierCatalogSyncService>();

        return services;
    }
}
