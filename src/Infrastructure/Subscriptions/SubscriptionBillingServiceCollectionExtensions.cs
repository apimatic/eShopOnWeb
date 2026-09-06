using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public static class SubscriptionBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers recurring-subscription billing backed by Maxio Advanced Billing, bound to the
    /// <c>Maxio</c> configuration section.
    ///
    /// Registration deliberately does not require valid credentials: a deployment without Maxio
    /// configured still starts, and the subscription endpoints report themselves unavailable rather than
    /// taking the whole API down. <see cref="MaxioOptions.Validate"/> is what turns missing configuration
    /// into that answer, at the point of first use.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();

        services.AddTransient<MaxioBaseAddressHandler>();
        services.AddHttpClient(MaxioClientFactory.HttpClientName)
            .AddHttpMessageHandler<MaxioBaseAddressHandler>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddSingleton<MaxioClientFactory>();
        services.AddSingleton<MaxioCatalog>();
        services.AddSingleton<SubscriberLockProvider>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
