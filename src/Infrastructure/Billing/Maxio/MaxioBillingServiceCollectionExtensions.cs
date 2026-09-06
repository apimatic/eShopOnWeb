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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/> client this integration owns. A named client, rather
    /// than the default one the SDK's own registration extension would take, so the timeout, the write
    /// guard and the connection lifetime configured here apply to Maxio traffic only.
    /// </summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddHttpClient(HttpClientName, (serviceProvider, httpClient) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                // Bounds one attempt, not the whole call. Left at its 100s default a hung provider would
                // pin a request thread for over a minute; the whole-call bound lives on the service.
                httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.HttpClientTimeoutSeconds));
            })
            .AddHttpMessageHandler(() => new MaxioWriteOnceHandler())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so it never returns to the factory for a rotated
                // handler; without this, a DNS change would be cached for the life of the process.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings));
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuropeanSite ? ServerEnvironment.Eu : ServerEnvironment.Us,

            // Maxio uses HTTP Basic with the API key as the username and a fixed placeholder password.
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = MaxioSettings.ApiKeyPasswordPlaceholder
            },

            Retry = RetryOptions.Default() with
            {
                MaxRetries = Math.Max(1, settings.MaxRetries),
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds))
            }
        };

        // The subdomain is substituted into the environment's URL template; an explicit BaseUrl replaces
        // that template outright and is used exactly as configured.
        if (settings.IsEuropeanSite)
        {
            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
            }

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!.Trim();
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            }

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!.Trim();
            }
        }

        return options;
    }
}
