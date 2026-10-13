using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

internal static class WalmartHttpClient
{
    public const string Name = "walmart";
}

/// <summary>
/// Registration for the Walmart Marketplace inventory integration: settings binding, a shared
/// HttpClient with transient-fault retry, the token provider (singleton, so tokens are cached),
/// and the inventory service.
/// </summary>
public static class WalmartServiceCollectionExtensions
{
    public static IServiceCollection AddWalmartIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WalmartSettings>(configuration.GetSection(WalmartSettings.SectionName));

        services.AddTransient<WalmartRetryHandler>();

        services.AddHttpClient(WalmartHttpClient.Name, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<WalmartRetryHandler>();

        // Singleton so the access token is cached and reused across requests.
        services.AddSingleton<IWalmartAccessTokenProvider, WalmartAccessTokenProvider>();
        services.AddScoped<IWalmartInventoryService, WalmartInventoryService>();

        return services;
    }
}
