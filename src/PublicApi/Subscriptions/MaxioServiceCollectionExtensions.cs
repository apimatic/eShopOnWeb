using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Composition for the Maxio billing integration: options bound from the "Maxio" configuration
/// section, a dedicated named HttpClient (own timeout/handlers, off the shared default client),
/// and the long-lived SDK client + billing service singletons.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    internal const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<MaxioWriteGuardHandler>();
        services.AddTransient<MaxioRequestLoggingHandler>();

        services
            .AddHttpClient(MaxioHttpClientName, client =>
            {
                // Bounds ONE attempt (per-attempt, like RetryOptions.Timeout); the whole-call
                // budget lives in MaxioSubscriptionBillingService.CallAsync.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<MaxioWriteGuardHandler>()
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton, so rotate pooled connections for DNS freshness.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(serviceProvider =>
        {
            var maxioOptions = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var logger = serviceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("MaxioAdvancedBilling.Retry");

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey ?? string.Empty,
                    Password = "x",
                },
                Retry = RetryOptions.Default() with
                {
                    // Per-attempt timeout; defaults (100s x attempts) are far too long for a request path.
                    Timeout = TimeSpan.FromSeconds(10),
                    OnRetry = attempt => logger.LogDebug(
                        "Maxio retry #{AttemptNumber} after {Delay}: {Reason}",
                        attempt.AttemptNumber,
                        attempt.Delay,
                        attempt.Reason),
                },
            };

            // The subdomain template is always set; an explicit Maxio:BaseUrl overrides it verbatim.
            clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl.Trim();
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
