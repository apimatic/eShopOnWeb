using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionBillingService"/> for DI.
    /// Binds only the "Maxio" config keys (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl) - no value is
    /// ever hard-coded here so the same build can target a different site/catalog.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // GetSection (not GetRequiredSection): an environment that never calls a subscription
        // endpoint (e.g. the existing test suite's WebApplicationFactory host) should not fail
        // startup just because it has no "Maxio" config - binding to an empty/default MaxioOptions
        // only surfaces as a failure if a subscription endpoint is actually invoked.
        services.Configure<MaxioOptions>(configuration.GetSection("Maxio"));

        // Registered over a named HttpClient (rather than the SDK's own AddMaxioAdvancedBillingClient
        // extension, which resolves the default/unnamed factory client) so this pipeline's timeout and
        // pooled-connection lifetime don't leak onto unrelated unnamed HttpClient consumers in this host.
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is registered as a singleton below and holds this HttpClient for the
                // app's lifetime, so IHttpClientFactory's normal handler rotation never reaches it -
                // recycle the pooled connections ourselves so a DNS change is eventually picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    // Bound a single attempt well under the default 100s; CreateCustomer/CreateSubscription
                    // are non-idempotent POSTs, so a hang here is the failure mode the find-or-create flow
                    // in MaxioSubscriptionBillingService exists to recover from.
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
