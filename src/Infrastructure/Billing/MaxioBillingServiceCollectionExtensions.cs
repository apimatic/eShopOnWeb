using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>The name of the dedicated <see cref="IHttpClientFactory"/> client backing the Maxio SDK.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing integration: validated settings (fail-fast at startup), a
    /// long-lived SDK client over a dedicated <see cref="IHttpClientFactory"/> client, and the
    /// <see cref="ISubscriptionBillingService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to start if any required credential is missing or blank (blank != missing,
        // so each part is checked with IsNullOrWhiteSpace). The message names the config key and never
        // echoes a value.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)} is not configured. " +
                "Set it via user-secrets, an environment variable, or your secret store before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain),
                $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)} is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)} is not configured.")
            .Validate(s => s.TimeoutSeconds is >= 1 and <= 600,
                $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.TimeoutSeconds)} must be between 1 and 600.")
            .ValidateOnStart();

        // A dedicated (named) HttpClient keeps this SDK's timeout/handler off the shared default client.
        // The per-attempt Timeout here is a backstop; the whole-call budget is enforced by a
        // CancellationToken deadline inside MaxioBillingService. PooledConnectionLifetime keeps DNS fresh
        // behind the long-lived singleton client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(100))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Options are built once, here, and captured in the singleton client: a rotated API key takes
            // effect only after a process restart (acceptable for this integration).
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                // Maxio Basic auth: username = API key, password = the literal "x".
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                // Per-attempt timeout; the total budget is a CancellationToken deadline in the service.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                // Assign LoggerFactory explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG env var can never
                // switch (unredacted) body logging on from outside the code. LogRequestBody stays off.
                Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false },
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Explicit override: use the configured base URL verbatim.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                // Derive the base URL from the subdomain: https://{site}.chargify.com
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<KeyedSemaphore>();
        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
