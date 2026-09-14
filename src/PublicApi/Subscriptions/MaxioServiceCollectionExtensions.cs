using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Wires the Maxio Advanced Billing client (fixed options, owned HttpClient) and the
/// <see cref="ISubscriptionService"/> boundary. Secrets are never hard-coded: the "Maxio" section is
/// bound from configuration (environment variables / user secrets) at startup.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.CONFIG_NAME);
        var maxioOptions = section.Get<MaxioOptions>() ?? new MaxioOptions();

        services.AddSingleton(maxioOptions);

        // A named HttpClient keeps this pipeline off the shared default client: the per-attempt timeout
        // is a backstop for a hung provider, and PooledConnectionLifetime keeps DNS fresh behind the
        // long-lived (singleton) SDK client.
        services.AddHttpClient(MaxioHttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                // Maxio Advanced Billing authenticates with HTTP Basic where the username is the API key
                // and the password is the literal "x".
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                },
                // One retry (2 attempts) at most, and a 10s per-attempt bound. Transport failures on a
                // write are still possible; idempotency is handled by the service (reference keys +
                // reconcile-on-ambiguity), not by blanket retries.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(maxioOptions.Subdomain))
            {
                clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain;
            }

            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl!;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
