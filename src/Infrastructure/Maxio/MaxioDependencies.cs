using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the subscription-billing capability: a named HttpClient for the Maxio Advanced
    /// Billing SDK, a lazily-built, long-lived client (with pooled-connection rotation), and the
    /// billing service. Credentials are read from configuration only — never from literals — and
    /// are validated when the first billing call is made, not at host startup, so hosts that do
    /// not use billing (and test hosts) run without Maxio configuration.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Named client: keeps the SDK's HTTP pipeline (timeout, handlers) off the shared default
        // factory client. PooledConnectionLifetime keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<MaxioClientProvider>();
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionService>();

        return services;
    }
}
