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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing SDK client and the application-facing
    /// <see cref="ISubscriptionBillingService"/>. Settings come from the "Maxio" configuration
    /// section (user-secrets / environment) — no value is hard-coded. The client is a singleton
    /// over a named, pooled <see cref="HttpClient"/> with an explicit timeout.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .PostConfigure(settings =>
            {
                var environment = configuration["MAXIO_ENVIRONMENT"];
                if (!string.IsNullOrWhiteSpace(environment))
                {
                    settings.Environment = environment;
                }
            });

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds a single attempt as a backstop; the per-operation total budget lives in
                // MaxioSubscriptionBillingService.CallAsync. The default 100s would pin requests
                // for minutes on a hung provider.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so keep pooled connections from pinning stale DNS.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            Validate(settings);

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void Validate(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured (set the MAXIO_API_KEY environment variable or a user-secret).");
        }
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured (set the MAXIO_SITE_SUBDOMAIN environment variable or a user-secret).");
        }
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured (set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or a user-secret).");
        }
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        var isEu = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                // Cap each attempt at 10s so a stalling provider cannot pin a request thread for minutes.
                Timeout = TimeSpan.FromSeconds(10)
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        // Verbatim base-address override: when configured, use it as the base address instead of
        // deriving one from the subdomain.
        var hasBaseUrlOverride = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (isEu)
        {
            var eu = options.Server.Production.Eu;
            if (hasBaseUrlOverride)
            {
                eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                eu.Site = settings.Subdomain;
            }
        }
        else
        {
            var us = options.Server.Production.Us;
            if (hasBaseUrlOverride)
            {
                us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                us.Site = settings.Subdomain;
            }
        }

        return options;
    }
}
