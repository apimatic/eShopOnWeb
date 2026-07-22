using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the billing provider for whichever host needs it. Both the storefront and the API compose
/// the integration through this single extension, so the provider stays configured in exactly one place.
/// </summary>
public static class BillingProviderServiceCollectionExtensions
{
    public static IServiceCollection AddBillingProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION));

        // The BaseAddress is resolved from configuration — an explicit Maxio:BaseUrl wins, otherwise the
        // host is derived from the site subdomain. Retargeting production / dev / a local mock is
        // therefore a configuration change and never a recompile.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
            httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });

        return services;
    }
}
