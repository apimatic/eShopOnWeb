using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings (with fail-fast validation), a
/// long-lived SDK client over a named <see cref="System.Net.Http.HttpClient"/>, and the
/// <see cref="ISubscriptionBillingService"/> implementation.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind settings from the Maxio: section and refuse to start if a required value is missing
        // or blank (each part checked independently) — rather than discovering it as a 401 later.
        services.AddOptions<MaxioSettings>()
            .Configure(options =>
            {
                options.ApiKey = configuration[$"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}"];
                options.Subdomain = configuration[$"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}"];
                options.ProductFamilyHandle = configuration[$"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}"];
                options.BaseUrl = configuration[$"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)}"];
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "Maxio:ApiKey is not configured. Set it via user-secrets or an environment variable before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain),
                "Maxio:Subdomain is not configured. Set it via user-secrets or an environment variable before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or an environment variable before starting the app.")
            .ValidateOnStart();

        // A named HttpClient keeps this pipeline off the shared default client. Timeout bounds a
        // single attempt; PooledConnectionLifetime keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived (holds the resilience pipelines and auth). Options are built
        // once here, so a rotated API key takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                // Basic auth (US/EU environments talk to chargify.com directly); the username is the
                // API key and the password is the literal "x".
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    Password = "x"
                }
            };

            // Base URL: an explicit override is used verbatim; otherwise derive from the subdomain.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            // Set the logger factory explicitly so the SDK's log environment variable cannot arm
            // request/response (incl. unredacted body) logging from outside the code.
            options.Logging = options.Logging with { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() };

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
