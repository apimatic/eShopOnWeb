using System;
using System.Linq;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription service.
    /// Credentials bind from the <c>Maxio:</c> configuration section; <c>Maxio:BaseUrl</c>,
    /// when set, is used verbatim in place of the URL derived from <c>Maxio:Subdomain</c>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
        {
            // Bounds a single attempt against Maxio; the whole call is additionally
            // bounded by the 30s budget inside SubscriptionService.
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        // The APIMatic-generated client is a lightweight wrapper over the shared HTTP
        // pipeline — one long-lived instance for the process lifetime.
        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = sp.GetRequiredApiKey(),
                    Password = "x"
                },
                Environment = ServerEnvironment.Us,
            };

            var maxio = sp.GetRequiredMaxioOptions();
            options.Server.Production.Us.Site = maxio.Subdomain!;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                options);
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }

    private static string GetRequiredApiKey(this IServiceProvider sp) =>
        sp.GetRequiredMaxioOptions().ApiKey
        ?? throw new InvalidOperationException(
            "Maxio:ApiKey is not configured. Provide it via user secrets or the MAXIO_API_KEY environment variable.");

    private static MaxioOptions GetRequiredMaxioOptions(this IServiceProvider sp)
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                "Either Maxio:Subdomain or Maxio:BaseUrl must be configured (MAXIO_SITE_SUBDOMAIN environment variable or user secrets).");
        }
        return options;
    }
}