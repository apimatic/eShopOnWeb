using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the Maxio billing seam. Both hosts call this so the provider is wired identically in the
/// storefront and in the API (plan.md §2.1, §4.3).
/// </summary>
public static class MaxioBillingDependencies
{
    /// <summary>How long a single outbound Maxio call may take before it is abandoned.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // The domain reads the plan and component handles through its own provider-neutral abstraction.
        services.AddSingleton<ISubscriptionSettings>(sp =>
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value);

        services.AddSingleton<MaxioCatalogCache>();

        // Typed client via IHttpClientFactory. The outbound target comes from configuration — an explicit
        // Maxio:BaseUrl wins, otherwise the host is derived from the subdomain and region — so the same
        // build can be pointed at production, a dev/sandbox tenant, or a local mock (plan.md §2.3, §4.3).
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
            http.Timeout = RequestTimeout;
        });

        // Reports at startup whether the configured catalog actually exists, without ever failing the host.
        services.AddHostedService<MaxioStartupValidator>();

        return services;
    }
}
