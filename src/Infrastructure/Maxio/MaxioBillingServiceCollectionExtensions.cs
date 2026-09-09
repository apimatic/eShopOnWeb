using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: the strongly-typed settings (fail-fast at
/// startup), the SDK client (a long-lived singleton over <see cref="IHttpClientFactory"/>), and the
/// <see cref="ISubscriptionBillingService"/> implementation.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind settings and refuse to boot when any required credential/identifier is missing or blank.
        // ValidateOnStart runs the checks at startup (not on the first request); the explicit guard
        // below is a second, dependency-free backstop that fails during service configuration.
        services
            .AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is not configured.")
            .ValidateOnStart();

        // The SDK options are built ONCE, here at registration, and captured in the singleton client —
        // a rotated key therefore takes effect on process restart. Secrets come from configuration
        // (user-secrets); no value is hard-coded.
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey) ||
            string.IsNullOrWhiteSpace(settings.Subdomain) ||
            string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle must all be configured " +
                "(via .NET user-secrets, environment variables, or another configuration source) before starting the app.");
        }

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;

            // Chargify/Maxio HTTP Basic: API key as the username, fixed literal "x" as the password.
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            };

            // BaseUrl override wins verbatim (EU/self-hosted/gateway); otherwise the site subdomain is
            // substituted into the default US template https://{site}.chargify.com.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            // Per-attempt timeout kept short for an interactive path; the whole-call budget lives in the
            // service's CancellationToken. POST writes are not resent (default HttpMethodsToRetry).
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) };

            // Leave LogRequestBody off; the DI extension fills LoggerFactory from the container, which
            // also disables the MAXIOADVANCEDBILLINGCLIENT_LOG environment variable from arming logging.
            options.Logging = new LoggingOptions();
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
