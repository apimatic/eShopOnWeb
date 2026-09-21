using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    // Own HttpClient name keeps this SDK's timeout / handler off the shared default factory client.
    private const string MaxioHttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers Maxio Advanced Billing: binds and validates <see cref="MaxioSettings"/> (fail-fast), wires the
    /// SDK client over a dedicated, pooled <see cref="HttpClient"/>, and registers <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.ConfigurationSection).Bind(settings);

        // Fail-fast: a missing credential is a deployment fault, not a per-request 401. Check every required
        // part (a blank part is not a missing one). Never echo the value.
        RequireConfigured(settings.ApiKey, $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.ApiKey)}");
        RequireConfigured(settings.Subdomain, $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.Subdomain)}");
        RequireConfigured(settings.ProductFamilyHandle, $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.ProductFamilyHandle)}");

        services.AddSingleton(settings);

        // Dedicated, pooled HttpClient: PooledConnectionLifetime keeps DNS fresh behind the long-lived
        // (singleton) SDK client; Timeout bounds a single attempt as a backstop (the real per-call budget
        // is enforced by a CancellationToken inside the service).
        services.AddHttpClient(MaxioHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    // The Maxio Basic scheme is API-key-as-username, password "x".
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Logging = new LoggingOptions
                {
                    // Assign explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG env var cannot force logging
                    // (incl. unredacted request bodies) on from outside the code.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Explicit override: used verbatim (a literal URL with no {site} token is left as-is).
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                // Derive from the subdomain: resolves https://{site}.chargify.com.
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private static void RequireConfigured(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets or an environment variable before starting the app.");
        }
    }
}
