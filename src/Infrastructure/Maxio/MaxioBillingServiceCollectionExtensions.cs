using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires Maxio Advanced Billing into the application: binds <see cref="MaxioSettings"/> from the
/// <c>Maxio:</c> section (fail-fast on missing credentials), registers the SDK client, and exposes the
/// <see cref="ISubscriptionBillingService"/> abstraction.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.SectionName).Bind(settings);

        // Credential fail-fast: a missing/blank required value is a deployment fault. Refuse to start rather
        // than discover it as a 401 on the first call. Each part is checked separately — a blank part is not
        // a missing one — and the value itself is never echoed.
        RequireConfigured(settings.ApiKey, "Maxio:ApiKey");
        RequireConfigured(settings.Subdomain, "Maxio:Subdomain");
        RequireConfigured(settings.ProductFamilyHandle, "Maxio:ProductFamilyHandle");

        services.AddSingleton(settings);

        // The SDK's DI extension owns the HttpClient (via IHttpClientFactory), assigns the LoggerFactory from
        // the container (which disables the MAXIOADVANCEDBILLINGCLIENT_LOG env-var body-logging backdoor), and
        // registers the client as a singleton. Options are captured once here, so a rotated key needs a
        // process restart.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;

            // Basic auth: the Chargify API key is the username; the password is the literal "x".
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!.Trim(),
                Password = "x",
            };

            // Base URL: use the explicit override verbatim when supplied, else derive it from the subdomain
            // via the default https://{site}.chargify.com template on the US Production server.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            }

            // Keep the per-attempt timeout tight; the whole-call budget is enforced in the service by a
            // CancellationToken (RetryOptions.Timeout bounds one attempt, not the whole call). POST/PATCH are
            // never resent under the default HttpMethodsToRetry, so no automatic duplicate writes.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) };
        });

        // Singleton so the per-user in-process subscribe gate is shared across requests (double-click safety).
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void RequireConfigured(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via environment variable, user-secrets, or your secret " +
                "store before starting the app.");
        }
    }
}
