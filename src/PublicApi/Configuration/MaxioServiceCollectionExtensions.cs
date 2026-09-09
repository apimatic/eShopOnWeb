using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the Maxio Advanced Billing subscription integration: settings binding, the typed HTTP
/// client, and the billing service. Wiring lives in the API host (which has the HTTP client
/// factory available); the reusable implementation lives in Infrastructure.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound from the "Maxio" section (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle,
        // Maxio:BaseUrl). Values come from user-secrets / environment configuration, never source.
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<MaxioApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
