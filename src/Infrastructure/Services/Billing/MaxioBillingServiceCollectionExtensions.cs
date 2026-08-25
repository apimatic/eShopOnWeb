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

namespace Microsoft.eShopWeb.Infrastructure.Services.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client (singleton, over a named, factory-managed
    /// HttpClient) and the subscription billing service. Settings bind from the "Maxio" section.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>().Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient(MaxioSettings.HttpClientName, client =>
            {
                // Bounds one attempt (backstop for a hung socket); the whole-call budget lives
                // in MaxioSubscriptionBillingService via a linked CancellationToken.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio:ApiKey is not configured. Provide it via user-secrets or the MAXIO_API_KEY environment variable.");
            }

            var isEu = string.Equals(settings.Environment, "Eu", StringComparison.OrdinalIgnoreCase);

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                if (isEu) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                else options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.Subdomain))
                {
                    throw new InvalidOperationException(
                        "Maxio:Subdomain is not configured. Provide it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
                }

                if (isEu) options.Server.Production.Eu.Site = settings.Subdomain;
                else options.Server.Production.Us.Site = settings.Subdomain;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioSettings.HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
