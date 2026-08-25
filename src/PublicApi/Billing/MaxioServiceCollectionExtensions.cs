using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // Named client keeps this pipeline (timeout, handler lifetime) off the shared default client.
        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the whole-call budget lives in MaxioBillingService.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable.");
            }

            var environment = (configuration["MAXIO_ENVIRONMENT"] ?? "US").Trim().ToUpperInvariant() switch
            {
                "US" => ServerEnvironment.Us,
                "EU" => ServerEnvironment.Eu,
                var other => throw new InvalidOperationException(
                    $"Unrecognized MAXIO_ENVIRONMENT value '{other}'. Expected 'US' or 'EU'.")
            };

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                if (environment == ServerEnvironment.Eu)
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                }
                else
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.Subdomain))
                {
                    throw new InvalidOperationException(
                        "Maxio:Subdomain is not configured. Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
                }

                if (environment == ServerEnvironment.Eu)
                {
                    options.Server.Production.Eu.Site = settings.Subdomain;
                }
                else
                {
                    options.Server.Production.Us.Site = settings.Subdomain;
                }
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        return services;
    }
}
