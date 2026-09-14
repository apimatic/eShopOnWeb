using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the <see cref="IMaxioBillingService"/>
    /// boundary. Credentials come from configuration (user-secrets / environment variables),
    /// never from source code.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl) || !string.IsNullOrWhiteSpace(o.Subdomain),
                "Either Maxio:BaseUrl or Maxio:Subdomain must be configured.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds a single hung attempt (a timeout is not retried by the SDK pipeline).
                c.Timeout = PerAttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                // The client below is a singleton, so keep pooled connections rotating for DNS freshness.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                // Shorten the SDK's default per-attempt timeout (100s) to match the HttpClient bound.
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey,
                    Password = "x",
                },
            };

            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = maxio.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
