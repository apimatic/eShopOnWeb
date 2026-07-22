using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription module. Both hosts call this so the storefront and the API talk to
/// the billing provider through exactly the same seam and the same configuration.
/// </summary>
public static class ConfigureSubscriptionServices
{
    public static IServiceCollection AddSubscriptionServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // The domain reasons about handles only; it reads them through this provider-agnostic view.
        services.AddSingleton<ISubscriptionCatalogSettings>(sp =>
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value);

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Typed client via IHttpClientFactory. The outbound target is resolved from configuration
        // so the same build can be pointed at production, a dev/sandbox tenant, or a local mock
        // server; an explicit Maxio:BaseUrl always wins over the subdomain-derived host. The host
        // is never hardcoded.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        return services;
    }
}
