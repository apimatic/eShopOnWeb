using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "maxio";

    /// <summary>
    /// Registers the Maxio-backed subscription billing services. Settings come from the "Maxio" section;
    /// missing settings do not fail startup, they surface as a 503 on the billing endpoints only.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioOptions.CONFIG_NAME).Get<MaxioOptions>() ?? new MaxioOptions();
        services.AddSingleton(settings);

        // Dedicated named client so timeouts/handlers don't leak into other HttpClient consumers.
        services.AddHttpClient(HTTP_CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton that holds one HttpClient: rotate connections for DNS changes.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IBillingGateway>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new MaxioBillingGateway(
                new Lazy<MaxioAdvancedBilling.MaxioAdvancedBillingClient>(
                    () => MaxioClientFactory.Create(settings, factory.CreateClient(HTTP_CLIENT_NAME))),
                settings.ProductFamilyHandle);
        });
        services.AddSingleton<ISubscriptionService, SubscriptionService>();
        return services;
    }
}
