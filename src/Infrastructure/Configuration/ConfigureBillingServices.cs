using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature. Both hosts call this so the storefront and the public API
/// share one domain service and one provider client.
/// </summary>
public static class ConfigureBillingServices
{
    /// <summary>
    /// Binds <see cref="MaxioSettings"/> and registers the subscription service over a typed
    /// <c>HttpClient</c> for the single Maxio client.
    /// </summary>
    /// <remarks>
    /// The client's <c>BaseAddress</c> is resolved from configuration — an explicit
    /// <c>Maxio:BaseUrl</c> wins, otherwise the host is derived from the subdomain and region — so
    /// the same build targets production, a dev/sandbox tenant, or a local mock without a code
    /// change. The host is never hardcoded.
    /// </remarks>
    public static IServiceCollection AddBillingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
