using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature. Both hosts call this so that the domain service, the
/// provider seam and the typed options are wired identically, and the billing provider stays
/// reachable through exactly one registration.
/// </summary>
public static class ConfigureSubscriptionServices
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION));
        services.Configure<SubscriptionSettings>(configuration.GetSection(SubscriptionSettings.CONFIG_SECTION));

        // The outbound target server comes from configuration, so the same build can be pointed at
        // production, a dev/sandbox tenant, or a local mock server. An explicit Maxio:BaseUrl wins
        // over the subdomain-derived host; the host is never hardcoded.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        services.AddScoped<IMeteredComponentValidator, MeteredComponentValidator>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
