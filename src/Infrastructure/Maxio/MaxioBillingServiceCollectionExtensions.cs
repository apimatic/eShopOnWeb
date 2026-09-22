using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription billing service. Fails fast at
    /// registration (before the host serves anything) when a required <c>Maxio:</c> setting is missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.ConfigurationSection).Bind(settings);

        // Credential fail-fast — each part checked separately (a blank part is not a missing one).
        Require(settings.ApiKey, $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.ApiKey)}");
        Require(settings.Subdomain, $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.Subdomain)}");
        Require(settings.ProductFamilyHandle, $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.ProductFamilyHandle)}");
        // Maxio:BaseUrl is an optional override — not required.

        services.AddSingleton(settings);

        // The SDK DI extension registers a singleton client over IHttpClientFactory and, because we leave
        // LoggingOptions at its defaults, fills LoggerFactory from the container (so logging goes through the
        // app's providers and the MAXIOADVANCEDBILLINGCLIENT_LOG env var cannot force unredacted body logging).
        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,   // validated non-blank above
                Password = "x"                 // Maxio Basic-auth password is always the literal "x"
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override — used as-is (a literal URL with no {site} token is not templated).
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                // Derive the Production base URL from the site subdomain.
                options.Server.Production.Us.Site = settings.Subdomain!;
            }
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;

        static void Require(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{key} is not configured. Set it via environment variable, user-secrets, or your " +
                    "secret store before starting the app.");
            }
        }
    }
}
