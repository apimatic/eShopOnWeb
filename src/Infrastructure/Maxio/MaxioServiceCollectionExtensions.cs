using System;
using System.Net.Http;
using global::Maxio;
using global::Maxio.Core.Authentication.Basic;
using global::Maxio.Core.Configuration;
using global::Maxio.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers Maxio subscription billing: the <see cref="MaxioClient"/> (over a scoped, named
/// <see cref="HttpClient"/>) and the <see cref="ISubscriptionBillingService"/> implementation.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named <see cref="HttpClient"/> so the SDK's pipeline is not shared with other consumers.</summary>
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: bind Maxio settings and refuse to start when a required credential is missing
        // or blank. ValidateOnStart makes the check fire during startup, not on the first request.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            // Every credential part is checked separately — a blank part is not a missing one.
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                "Maxio:ApiKey is not configured. Set it via user-secrets or environment before starting.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain),
                "Maxio:Subdomain is not configured. Set it via user-secrets or environment before starting.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or environment before starting.")
            .Validate(s => s.TimeoutSeconds is >= 1 and <= 600,
                "Maxio:TimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        // Named HttpClient: scoped pipeline, connection recycling (keeps DNS fresh behind the
        // long-lived singleton client), and a backstop per-attempt timeout.
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        // Single, long-lived MaxioClient. Options are built once here and captured in the singleton;
        // a rotated API key therefore takes effect only on process restart (documented behaviour).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            // Backstop attempt timeout on the HttpClient itself (bounds a single hung socket on any verb).
            httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            var options = new MaxioClientOptions
            {
                // Sandbox/production hosting is US (Chargify). Basic auth is valid only for US/EU.
                Environment = ServerEnvironment.Us,
                // Basic auth: username = Chargify API key, password = literal "x".
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                // Per-attempt timeout, kept at or under the total budget. The whole-call budget is
                // enforced by the service via a CancellationToken (SDK Timeout is per attempt only).
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(Math.Min(settings.TimeoutSeconds, 30)),
                },
                // Assign LoggerFactory explicitly so the MAXIOCLIENT_LOG env var cannot switch
                // unredacted request-body logging on from outside the code. LogRequestBody stays off,
                // so customer PII (email/name) in request bodies is never written to logs.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetService<ILoggerFactory>(),
                },
            };

            // Base URL: an explicit override is used verbatim; otherwise the subdomain feeds the
            // default https://{site}.chargify.com template.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            return new MaxioClient(httpClient, options);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
