using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires the Maxio Advanced Billing client and the subscription service into the container.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // Own the HttpClient lifetime explicitly so the SDK client can be a long-lived singleton
        // (per dotnet-client-initialization): a bounded per-attempt Timeout stops a hung provider
        // from pinning a request thread, and PooledConnectionLifetime keeps DNS fresh behind the
        // singleton so an IP change is not cached for the process lifetime.
        services.AddHttpClient(HttpClientName, (sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException(
                    "Maxio is not configured. Set Maxio:ApiKey and Maxio:Subdomain (via user-secrets from " +
                    "MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN) before using the subscription endpoints.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                }
            };

            // Site derives the base URL as https://{subdomain}.chargify.com; an explicit BaseUrl,
            // when supplied, overrides that per-server/node value verbatim.
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        // Singleton so the per-user gate is shared across the scoped service instances.
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
