using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription module. Called from each host's composition root (plan.md §2.1/§4.3)
/// so the Web storefront and the PublicApi share one domain service and one provider client.
/// </summary>
public static class SubscriptionServiceExtensions
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION));
        services.AddScoped<ISubscriptionSettings>(sp =>
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value);
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Typed client via IHttpClientFactory. The BaseAddress comes from configuration so the SAME
        // build can target production, a dev/sandbox tenant, or a local mock server — an explicit
        // Maxio:BaseUrl wins, otherwise the host is derived from Maxio:Subdomain (plan.md §2.3).
        // The host is never hardcoded here.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        return services;
    }
}
