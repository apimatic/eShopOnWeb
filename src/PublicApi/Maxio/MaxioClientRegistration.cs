using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioClientRegistration
{
    // Named client keeps this pipeline (timeout, handler lifetime) off the shared default HttpClient.
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the SDK retry pipeline sits above SendAsync. Default 100s is an outage, not a timeout.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind the long-lived pipeline.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable " +
                    "or the 'Maxio:ApiKey' user-secret.");
            }

            var environment = string.Equals(settings.Environment, "eu", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us;

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
                    Timeout = TimeSpan.FromSeconds(10) // per attempt
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override (mock server / proxy / alternate site).
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
                        "Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable " +
                        "or the 'Maxio:Subdomain' user-secret.");
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

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
