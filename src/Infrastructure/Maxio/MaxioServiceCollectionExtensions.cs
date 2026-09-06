using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the
    /// "Maxio" configuration section.
    /// </summary>
    /// <remarks>
    /// Registration deliberately does not fail when the section is absent. Subscription billing is
    /// an additive capability: the rest of eShopOnWeb has to keep starting and serving without
    /// billing credentials. A caller that reaches a subscription endpoint on an unconfigured
    /// deployment gets a 503 naming the missing configuration keys (never their values).
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSectionName));
        services.AddMemoryCache();

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, httpClient) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptionsMonitor<MaxioSettings>>().CurrentValue;

                // Null when the site is not configured yet; MaxioApiClient turns that into a clear
                // error rather than an obscure "invalid request URI".
                httpClient.BaseAddress = settings.ResolveBaseAddress();

                // Bounds the whole call, retries included.
                httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 300));
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
