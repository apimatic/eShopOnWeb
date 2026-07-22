using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature for a host. Shared by the Web storefront and the
/// PublicApi so both compose the same domain service over the same provider seam
/// (mirrors <see cref="Dependencies"/>).
/// </summary>
public static class ConfigureSubscriptionServices
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION));

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Typed client via IHttpClientFactory. The base address comes from configuration so the
        // same build can target production, a dev/sandbox tenant, or a local mock — an explicit
        // Maxio:BaseUrl always wins over the subdomain-derived host. Never hardcode the host.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        return services;
    }
}
