using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature. Both hosts call this from their composition root — the
/// storefront through <c>AddCoreServices</c> and the API from <c>Program</c> — so the provider is
/// wired identically in each and stays touched in exactly one class (§2.1, §4.3).
/// </summary>
public static class SubscriptionDependencies
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

        services.AddSingleton<MaxioComponentValidationCache>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // The outbound target is resolved from configuration, never hardcoded: an explicit
        // Maxio:BaseUrl wins, otherwise the host is derived from the subdomain and region. That is
        // what lets the same build hit production, a dev tenant or a local mock (§2.3).
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
            http.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
