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

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named <see cref="IHttpClientFactory"/> client backing the Maxio SDK, so its pipeline
    /// (timeout, connection lifetime) is scoped to this SDK and not shared with other consumers.</summary>
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers Maxio Advanced Billing: fail-fast options validation, the SDK client (as a
    /// long-lived singleton over a dedicated <see cref="IHttpClientFactory"/> client), and the
    /// <see cref="ISubscriptionBillingService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // (1) Fail-fast credentials: bind + validate each required key (missing OR blank) separately at
        // startup, naming the config key without ever echoing its value.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSection))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                "Maxio:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain),
                "Maxio:Subdomain is not configured. Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable before starting the app.")
            .ValidateOnStart();

        // A dedicated HttpClient: a per-attempt Timeout backstop and a pooled-connection lifetime so a
        // long-lived (singleton) SDK client does not pin DNS. The SDK owns retries/auth on top of this.
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // (2) Options are built once here and captured in the singleton client — a rotated ApiKey
        // requires a process restart (documented). LoggerFactory is set explicitly so the
        // MAXIOADVANCEDBILLINGCLIENT_LOG environment variable cannot switch request-body logging on.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x" // Maxio Basic auth: username = API key, password = literal "x".
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false // request bodies carry PII (name/email) — never log verbatim.
                }
            };

            // Point the US Production server at the configured site, or the verbatim BaseUrl override.
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
