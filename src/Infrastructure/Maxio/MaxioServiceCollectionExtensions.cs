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
/// Registers Maxio Advanced Billing subscription billing: settings (fail-fast validated), the SDK client
/// (single long-lived instance over a dedicated <see cref="IHttpClientFactory"/> client), and the
/// <see cref="ISubscriptionBillingService"/> implementation.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>The dedicated named HttpClient for the Maxio SDK, kept off the shared default client.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: bind Maxio:* and refuse to start if any required credential is missing/blank. This runs
        // synchronously during service registration — before the app serves anything — so a missing secret
        // surfaces here, not later as a 401 from the provider on the first request. Each part is checked
        // individually because a blank part is not the same as a missing one.
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();
        RequireConfigured(settings.ApiKey, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}");
        RequireConfigured(settings.Subdomain, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}");
        RequireConfigured(settings.ProductFamilyHandle, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}");

        // Dedicated HttpClient: per-attempt timeout backstop + connection recycling so the long-lived
        // (singleton) SDK client does not cache DNS forever.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Single long-lived SDK client. Options are built ONCE here and captured in the singleton, so a
        // rotated API key takes effect only on process restart (documented behaviour for this sandbox).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                // US region; Basic auth (username = API key, password = "x") per the SDK's Servers & auth.
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                // Per-attempt timeout; the whole-call budget is enforced with a CancellationToken in the service.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // Base URL: explicit override verbatim, else derive from the site subdomain
            // (template https://{site}.chargify.com).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            // Assign the logger factory explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG environment
            // variable cannot switch request-body logging on from outside the code. Request bodies are
            // never logged (LogRequestBody stays off) because the customer payload carries PII.
            options.Logging = options.Logging with { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() };

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void RequireConfigured(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it (via environment variable, user-secrets, or your secret " +
                "store) before starting the application.");
        }
    }
}
