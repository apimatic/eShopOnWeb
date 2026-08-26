using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioBillingDependencies
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription billing service.
    /// Configuration is read from the "Maxio" section (Maxio:ApiKey, Maxio:Subdomain,
    /// Maxio:ProductFamilyHandle, optional Maxio:BaseUrl), falling back to the
    /// MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY environment variables.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();

            string? Get(string key, string environmentFallback)
            {
                var value = configuration[key];
                return string.IsNullOrWhiteSpace(value) ? configuration[environmentFallback] : value;
            }

            var options = new MaxioOptions
            {
                ApiKey = Get("Maxio:ApiKey", "MAXIO_API_KEY"),
                Subdomain = Get("Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN"),
                ProductFamilyHandle = Get("Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY"),
                BaseUrl = configuration["Maxio:BaseUrl"]
            };

            if (string.IsNullOrWhiteSpace(options.ApiKey)
                || string.IsNullOrWhiteSpace(options.Subdomain)
                || string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            {
                throw new InvalidOperationException(
                    "Maxio billing is not configured. Provide Maxio:ApiKey, Maxio:Subdomain and " +
                    "Maxio:ProductFamilyHandle via user-secrets, or set the MAXIO_API_KEY, " +
                    "MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY environment variables.");
            }

            return options;
        });

        // Named client: keeps the timeout and handler pipeline scoped to this SDK.
        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the SDK's own per-attempt timeout and the service-level
                // call budget sit on top of this.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<MaxioOptions>();
            var isEu = string.Equals(
                sp.GetRequiredService<IConfiguration>()["MAXIO_ENVIRONMENT"], "EU", StringComparison.OrdinalIgnoreCase);

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = cfg.ApiKey!, Password = "x" },
                Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };

            if (isEu)
            {
                options.Server.Production.Eu.Site = cfg.Subdomain!;
                if (!string.IsNullOrWhiteSpace(cfg.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = cfg.BaseUrl;
                }
            }
            else
            {
                options.Server.Production.Us.Site = cfg.Subdomain!;
                if (!string.IsNullOrWhiteSpace(cfg.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = cfg.BaseUrl;
                }
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }
}
