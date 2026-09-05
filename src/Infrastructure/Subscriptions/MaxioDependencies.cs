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

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public static class MaxioDependencies
{
    private const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<MaxioOptions>(configuration.GetSection("Maxio"));

        // Named client (not the shared default one) so this SDK's timeout/handler never affects
        // any other unnamed HttpClient consumer in the app.
        services.AddHttpClient(MaxioHttpClientName, httpClient =>
            {
                httpClient.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };
            clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        // Singleton: the client above is process-wide, and the service caches the resolved
        // product-family id (handles are stable; the numeric id behind them is not, so it is
        // resolved once per process rather than hard-coded).
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
    }
}
