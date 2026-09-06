using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Bounds a single HTTP attempt as a backstop beneath the SDK's own per-attempt timeout. The framework
    /// default is 100s, which would let a hung provider pin a request thread for over a minute.
    /// </summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Keeps DNS fresh behind the long-lived client, which resolves its <see cref="HttpClient"/> once.
    /// </summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers recurring-subscription billing backed by Maxio Advanced Billing.
    /// </summary>
    /// <remarks>
    /// Registration never fails on missing configuration: the settings are validated when the client is first
    /// built, so a deployment without Maxio credentials still serves the rest of the API and only the
    /// subscription endpoints report themselves as unavailable.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMemoryCache();
        services.AddTransient<MaxioWriteOnceHandler>();

        // A named client, so the timeout, the primary handler and the write-once handler apply to Maxio only
        // and not to every other unnamed HttpClient consumer in the application.
        services.AddHttpClient(MaxioClientProvider.HttpClientName, client => client.Timeout = HttpClientTimeout)
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = PooledConnectionLifetime
            });

        services.AddSingleton<IMaxioClientProvider, MaxioClientProvider>();
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
