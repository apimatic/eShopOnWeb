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
    private const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionBillingService"/>.
    /// Binds settings from the <c>Maxio:</c> configuration section and <b>fails fast at startup</b>
    /// (throws here, during service configuration) when a required credential is missing — rather
    /// than surfacing it as a 401 on the first call. Secret values are never logged.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings
        {
            ApiKey = configuration["Maxio:ApiKey"],
            Subdomain = configuration["Maxio:Subdomain"],
            ProductFamilyHandle = configuration["Maxio:ProductFamilyHandle"],
            BaseUrl = configuration["Maxio:BaseUrl"],
        };
        if (int.TryParse(configuration["Maxio:RequestTimeoutSeconds"], out var timeout) && timeout > 0)
            settings.RequestTimeoutSeconds = timeout;

        // Fail-fast: each required part checked separately (a blank part is not a missing one).
        RequireConfigured(settings.ApiKey, "Maxio:ApiKey");
        RequireConfigured(settings.Subdomain, "Maxio:Subdomain");
        RequireConfigured(settings.ProductFamilyHandle, "Maxio:ProductFamilyHandle");

        services.AddSingleton(settings);

        // A named HttpClient keeps this pipeline off the shared default client. PooledConnectionLifetime
        // recycles connections so a long-lived singleton client does not cache DNS indefinitely.
        services.AddHttpClient(HttpClientName, c =>
            {
                // Per-attempt backstop; the whole-call budget is enforced by the service's CancellationToken.
                c.Timeout = TimeSpan.FromSeconds(Math.Max(settings.RequestTimeoutSeconds, 30));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    // The username is the Maxio (Chargify) API key; the password is the literal "x".
                    Username = settings.ApiKey!,
                    Password = "x"
                },
                // Assign LoggerFactory explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG env var cannot
                // arm request-body logging from outside the code; keep LogRequestBody off (default).
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                },
                // SDK Timeout is per attempt; cap it so a hung attempt is short and the service's
                // whole-call budget stays meaningful across retries.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Optional verbatim override — used as-is instead of deriving from the subdomain.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                // {site} in https://{site}.chargify.com
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    private static void RequireConfigured(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via environment variable, user-secrets, or your " +
                "secret store before starting the app.");
        }
    }
}
