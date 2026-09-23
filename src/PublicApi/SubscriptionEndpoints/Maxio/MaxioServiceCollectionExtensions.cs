using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing subscription capability: validated settings, the SDK client
/// over an <see cref="IHttpClientFactory"/>-managed <see cref="HttpClient"/>, and the service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    // Per-attempt bound (the SDK's own timeout is per attempt, not total; the service adds a total
    // deadline via a linked CancellationToken).
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(20);

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        // --- Fail-fast settings: the host refuses to start if any required credential is missing/blank ---
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is not configured.")
            .ValidateOnStart();

        // --- A named HttpClient, isolated from the shared default client, with a real timeout and a
        //     connection-lifetime so DNS stays fresh behind the long-lived singleton client. ---
        services.AddHttpClient(HttpClientName, c => c.Timeout = PerAttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // --- The SDK client: options built ONCE at registration and captured in the singleton. ---
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                // Sandbox is US-hosted chargify.com; Basic auth (API key + literal "x") works there.
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                // Set LoggerFactory explicitly so the SDK's MAXIOADVANCEDBILLINGCLIENT_LOG env var cannot
                // switch body logging on from outside the code; LogRequestBody stays off.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!; // verbatim override
            else
                options.Server.Production.Us.Site = settings.Subdomain;   // -> https://{Subdomain}.chargify.com

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
