using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>Named <see cref="System.Net.Http.HttpClient"/> registrations for the Walmart integration.</summary>
public static class WalmartHttpClients
{
    public const string Auth = "Walmart.Auth";
}

public static class WalmartServiceCollectionExtensions
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the Walmart Marketplace inventory integration: settings bound from the
    /// <c>Walmart:</c> configuration section, a token provider (singleton, so the access token is
    /// cached across requests), and the inventory service.
    /// </summary>
    public static IServiceCollection AddWalmartInventoryIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WalmartSettings>()
            .Bind(configuration.GetSection(WalmartSettings.ConfigSection));

        // Dedicated HttpClient for the auth/token endpoint, used by the singleton token provider.
        services.AddHttpClient(WalmartHttpClients.Auth, c => c.Timeout = HttpTimeout);

        services.AddSingleton<IWalmartTokenProvider, WalmartTokenProvider>();

        // Typed client for inventory calls; transient (no shared state) with a pooled handler.
        services.AddHttpClient<IWalmartInventoryService, WalmartInventoryService>(c => c.Timeout = HttpTimeout);

        return services;
    }
}
