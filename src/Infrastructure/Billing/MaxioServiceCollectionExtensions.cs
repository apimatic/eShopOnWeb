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

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named HttpClient for Maxio, keeping its timeout/handler pipeline off the shared default client.</summary>
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .PostConfigure<IConfiguration>((settings, config) =>
            {
                // Fall back to the flat environment variables when the section is not populated.
                if (string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    settings.ApiKey = config["MAXIO_API_KEY"] ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(settings.Subdomain))
                {
                    settings.Subdomain = config["MAXIO_SITE_SUBDOMAIN"] ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
                {
                    settings.ProductFamilyHandle = config["MAXIO_DEFAULT_PRODUCT_FAMILY"] ?? string.Empty;
                }
            });

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the whole-call budget lives in MaxioSubscriptionService.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var config = sp.GetRequiredService<IConfiguration>();

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio billing is not configured: set 'Maxio:ApiKey' (e.g. via the MAXIO_API_KEY environment variable or user-secrets).");
            }
            if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException(
                    "Maxio billing is not configured: set 'Maxio:Subdomain' (e.g. via the MAXIO_SITE_SUBDOMAIN environment variable or user-secrets).");
            }

            var isEu = string.Equals(config["MAXIO_ENVIRONMENT"], "EU", StringComparison.OrdinalIgnoreCase);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x" // Maxio API keys authenticate as the Basic username; password is a fixed placeholder.
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10) // per attempt
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

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
