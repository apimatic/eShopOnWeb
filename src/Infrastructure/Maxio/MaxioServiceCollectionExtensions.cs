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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires the Maxio Advanced Billing SDK and the subscription-billing service into DI. The client is
/// constructed over a named <see cref="IHttpClientFactory"/> client (rather than the SDK's DI extension)
/// so logging is set explicitly (PII-safe) and the HTTP pipeline is scoped to this SDK.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    internal const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt timeout for a single Maxio HTTP attempt (retries apply on top; see the service for the total budget).</summary>
    internal static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>HttpClient backstop timeout — above the service's total per-call budget so the request-scoped budget wins.</summary>
    internal static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(35);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: bind Maxio settings and refuse to start if a required credential is missing/blank.
        // Each part is checked separately (a blank part is not a missing one) with a message that names the key.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                $"{MaxioSettings.SectionName}:ApiKey is not configured. Set it via user-secrets or environment.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain),
                $"{MaxioSettings.SectionName}:Subdomain is not configured. Set it via user-secrets or environment.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                $"{MaxioSettings.SectionName}:ProductFamilyHandle is not configured. Set it via user-secrets or environment.")
            .ValidateOnStart();

        // A named HttpClient keeps this SDK's timeout/handler off the app's shared default client.
        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Recycle pooled connections on a timer so a long-lived singleton client never caches stale DNS.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                // Basic auth: username = API key, password = literal "x" (US/EU chargify.com only).
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Environment = ServerEnvironment.Us,
                // Per-attempt timeout; the total call budget is enforced by a CancellationToken in the service.
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                // Set LoggerFactory explicitly and keep LogRequestBody OFF: CreateCustomer carries email (PII),
                // and an explicit factory disarms the MAXIOADVANCEDBILLINGCLIENT_LOG env var from turning bodies on.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            // Bind the {site} template var from config; an explicit BaseUrl overrides the derived address verbatim.
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
