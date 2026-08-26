using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        // Named client keeps this pipeline (timeout, handler lifetime) off the shared default client.
        services.AddHttpClient(MaxioBillingService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio:ApiKey is not configured. Provide it via the MAXIO_API_KEY environment variable or the Maxio:ApiKey user secret.");
            }

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }
            else if (!string.IsNullOrWhiteSpace(options.Subdomain))
            {
                clientOptions.Server.Production.Us.Site = options.Subdomain;
            }
            else
            {
                throw new InvalidOperationException(
                    "Maxio:Subdomain is not configured. Provide it via the MAXIO_SITE_SUBDOMAIN environment variable or the Maxio:Subdomain user secret.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioBillingService.HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<ISubscriptionUserContextAccessor, SubscriptionUserContextAccessor>();
        services.AddScoped<MaxioBillingService>();

        return services;
    }
}
