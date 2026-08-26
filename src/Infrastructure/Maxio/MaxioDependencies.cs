using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers the Maxio SDK client (singleton over a named, factory-managed HttpClient)
    /// and the <see cref="ISubscriptionBillingService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop; the SDK retry pipeline sits above SendAsync, so each
                // attempt gets a fresh full timeout. The whole-call budget lives in the service.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton, so factory handler rotation never applies;
                // keep DNS fresh behind the long-lived client instead.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException(
                    "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain " +
                    "(user-secrets or environment variables MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN).");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var isEu = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
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

            if (isEu)
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                }
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                }
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionService>();
        return services;
    }
}
