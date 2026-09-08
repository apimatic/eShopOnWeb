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
/// Wires the Maxio Advanced Billing client and the billing service into DI.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the SDK client wraps (kept off the shared default client).</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);

        // A named HttpClient so this SDK's timeout/handler stay scoped to it (not the app's default client).
        // Timeout bounds a single attempt; the whole call is bounded by a CancellationToken inside the service.
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so IHttpClientFactory handler rotation never
                // reaches it — keep pooled connections (and DNS) fresh explicitly.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is lightweight controller wrappers over the shared HTTP pipeline — construct once.
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            return BuildClient(httpClient, settings);
        });

        services.AddSingleton<IMaxioBillingService, MaxioBillingService>();

        return services;
    }

    /// <summary>
    /// Builds a configured <see cref="MaxioAdvancedBillingClient"/>: Basic auth (API key as username,
    /// literal "x" as password) on the US environment, with the base address taken either from an explicit
    /// <see cref="MaxioSettings.BaseUrl"/> override or derived from <see cref="MaxioSettings.Subdomain"/>.
    /// </summary>
    internal static MaxioAdvancedBillingClient BuildClient(HttpClient httpClient, MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException("Either Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        if (hasBaseUrl)
        {
            // Used verbatim as the API base address.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            // Derives https://{site}.chargify.com from the subdomain.
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
