using System;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the
    /// <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Configuration is deliberately not validated at start-up: the rest of eShopOnWeb must keep
    /// working when no billing credentials are present. Invalid configuration surfaces as a
    /// <see cref="ApplicationCore.Exceptions.BillingConfigurationException"/> the first time a
    /// subscription endpoint is used, which the API maps to <c>503 Service Unavailable</c>.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddSingleton<MaxioSiteMetadataCache>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services
            .AddHttpClient<IMaxioClient, MaxioClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
