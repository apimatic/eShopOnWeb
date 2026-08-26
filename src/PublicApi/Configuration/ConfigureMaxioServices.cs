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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the Maxio Advanced Billing client and the subscription billing service.
/// Settings bind from the "Maxio" configuration section (see <see cref="MaxioOptions"/>);
/// secrets arrive via user-secrets or environment variables, never from this repo.
/// </summary>
public static class ConfigureMaxioServices
{
    public const string MaxioHttpClientName = "Maxio";

    public static IServiceCollection AddMaxioServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.CONFIG_NAME));

        services.AddHttpClient(MaxioHttpClientName, client =>
            {
                // Per-attempt backstop: bounds a hung provider. The whole-call
                // budget lives in MaxioSubscriptionService (linked token).
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so handler rotation never
                // reaches it — keep DNS fresh on the pooled connections instead.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            return CreateClient(httpClient, maxioOptions);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioOptions maxioOptions)
    {
        if (string.IsNullOrWhiteSpace(maxioOptions.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Set 'Maxio:ApiKey' (from the MAXIO_API_KEY environment variable, e.g. via .NET user-secrets).");
        }

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                // Per attempt; the default (100s) would pin a request thread for
                // minutes on a stalling provider.
                Timeout = TimeSpan.FromSeconds(10)
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxioOptions.ApiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
        }
        else if (!string.IsNullOrWhiteSpace(maxioOptions.Subdomain))
        {
            clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain;
        }
        else
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Set 'Maxio:Subdomain' (from MAXIO_SITE_SUBDOMAIN) or 'Maxio:BaseUrl'.");
        }

        return new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }
}
