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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    // Bounds one attempt. The whole-call budget is enforced separately with a CancellationToken
    // deadline inside MaxioBillingService (Timeout here is per attempt, not total).
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers the Maxio subscription-billing integration: fail-fast settings validation, a
    /// long-lived <see cref="MaxioAdvancedBillingClient"/> over a named <see cref="IHttpClientFactory"/>
    /// client, and the <see cref="ISubscriptionBillingService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: bind Maxio: and refuse to start if a required credential is missing/blank.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // Named HttpClient — keeps this pipeline (timeout, primary handler) off the shared default
        // client. PooledConnectionLifetime lets a long-lived (singleton) SDK client pick up DNS
        // changes; the per-attempt Timeout stops a hung provider from holding the request open.
        services.AddHttpClient(HttpClientName, c => c.Timeout = PerAttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is a singleton: its options (and thus credentials) are captured ONCE here,
        // so a rotated key takes effect only after a process restart — acceptable for this integration.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,   // validated non-blank at startup
                    Password = "x"                 // per Maxio Basic-auth: key as username, "x" as password
                },
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Assign LoggerFactory explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG env var
                    // cannot switch body logging on from outside the code. Request bodies carry
                    // customer PII (email/name), so LogRequestBody stays OFF.
                    LoggerFactory = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // BaseUrl override wins verbatim; otherwise derive the Production/US base URL from the subdomain.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            else
                options.Server.Production.Us.Site = settings.Subdomain!;

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }
}
