using System;
using FirecrawlApi;
using FirecrawlApi.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Firecrawl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Suppliers;

/// <summary>
/// Registers everything the supplier-catalog sync feature needs: the Firecrawl client (bound from the
/// <c>Firecrawl</c> configuration section), the reader, the sync processor, and the queue plus its
/// background worker.
/// </summary>
public static class SupplierSyncServiceCollectionExtensions
{
    public static IServiceCollection AddSupplierCatalogSync(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(FirecrawlSettings.SectionName).Get<FirecrawlSettings>() ?? new FirecrawlSettings();
        services.Configure<FirecrawlSettings>(configuration.GetSection(FirecrawlSettings.SectionName));

        services.AddFirecrawlApiClient(options =>
        {
            options.BearerAuth = settings.ApiKey;
            options.Environment = ServerEnvironment.Production;

            // Firecrawl:BaseUrl is an optional override; when set it is used verbatim for every call.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl.Trim();
        });

        services.AddScoped<ISupplierCatalogReader, FirecrawlSupplierCatalogReader>();
        services.AddScoped<ISupplierSyncProcessor, SupplierSyncProcessor>();
        services.AddScoped<ISupplierSyncService, SupplierSyncService>();
        services.AddSingleton<ISupplierSyncQueue, ChannelSupplierSyncQueue>();
        services.AddHostedService<SupplierSyncBackgroundService>();

        return services;
    }
}
