using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Wires the subscription feature into a host. Both composition roots — the storefront's
/// <c>AddCoreServices</c> and the public API's <c>Program</c> — call this, so the two hosts share one
/// domain service and one provider client.
/// </summary>
public static class BillingServiceExtensions
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(
            configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        services.AddSingleton<ISubscriptionCatalogSettings>(sp =>
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value);

        // A typed client from IHttpClientFactory: one pooled, long-lived HttpClient rather than one
        // per request. The outbound target server is resolved from configuration inside the client
        // itself, so retargeting production, a dev tenant, or a local mock never needs a code change.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            if (settings.TryResolveBaseUrl(out var baseUrl))
            {
                http.BaseAddress = new Uri(baseUrl);
            }
        });

        // The provisioning surface is the same single class, exposed only to the seeding tool.
        services.AddTransient<IBillingProvisioningClient>(sp =>
            (MaxioBillingClient)sp.GetRequiredService<IBillingClient>());

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }

    /// <summary>
    /// Adds the startup check that reports whether the configured metered component resolves. It
    /// only logs, so a billing misconfiguration can never stop a host from starting.
    /// </summary>
    public static IServiceCollection AddSubscriptionCatalogValidation(this IServiceCollection services)
    {
        services.AddHostedService<SubscriptionCatalogValidator>();

        return services;
    }
}
