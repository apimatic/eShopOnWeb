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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing client and the <see cref="ISubscriptionBillingService"/>.
/// The Maxio client is built once and held as a long-lived singleton over a named
/// <see cref="IHttpClientFactory"/> client (its own timeout + pooled handler), so retries, auth and
/// the resilience pipeline are constructed once rather than per request.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>The name of the dedicated <see cref="HttpClient"/> the Maxio client runs on.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    // Per-attempt hard backstop on a hung socket. The whole-call bound is a CancellationToken
    // deadline applied inside MaxioBillingService; see that type's Bounded helper.
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + validate; ValidateOnStart turns a missing credential into a startup failure rather
        // than a 401 on the first request in production.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)} is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)} is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)} is required.")
            .ValidateOnStart();

        // Explicit fail-fast guard with an operator-actionable message (never echoes the value).
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        GuardRequired(settings.ApiKey, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}");
        GuardRequired(settings.Subdomain, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}");
        GuardRequired(settings.ProductFamilyHandle, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}");

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Recycle pooled connections so a long-lived singleton client does not cache DNS forever.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                // Basic auth: username = Chargify API key, password = the constant "x".
                BasicAuth = new BasicAuthCredentials { Username = s.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                // Assign LoggerFactory explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG env var cannot
                // switch on unredacted request-body logging (customer PII) from outside the code.
                // LogRequestBody stays off (the default).
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() }
            };

            // Production/US base URL: https://{site}.chargify.com with {site} = the configured subdomain.
            options.Server.Production.Us.Site = s.Subdomain;

            // Optional verbatim base-URL override (e.g. a gateway or mock).
            if (!string.IsNullOrWhiteSpace(s.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = s.BaseUrl!;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    private static void GuardRequired(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets, an environment variable, or your " +
                "secret store before starting the app.");
        }
    }
}
