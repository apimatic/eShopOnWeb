using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature. Both hosts call this from their own composition root so the
/// storefront and the API drive the provider through exactly the same, identically configured seam.
/// </summary>
public static class ConfigureSubscriptionServices
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // A typed client, so the HttpClient is pooled and long-lived rather than created per call.
        // The base address is resolved from configuration — an explicit Maxio:BaseUrl wins over the
        // subdomain-derived host — so the same build targets production, a sandbox tenant or a local
        // mock without a code change (plan.md §2.3/§4.3). Resolution is deferred to first use so a
        // host with no Maxio configuration still starts normally.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
