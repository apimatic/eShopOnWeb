using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription module. Both hosts call this from their composition root (§2.1) so
/// the storefront and the PublicApi share one billing client and one service implementation.
/// </summary>
public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // The outbound target is resolved from configuration, never hardcoded: an explicit
        // Maxio:BaseUrl wins, otherwise the host is derived from the subdomain and region. That is
        // what lets the same build point at production, a sandbox tenant, or a local mock (§2.3/§4.3).
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = settings.ResolveBaseUrl();
            httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30);
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
