using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription-billing service. Binds the
    /// <c>Maxio:</c> configuration section and fails fast at startup if a required credential is missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        // Fail fast: refuse to wire up billing with a missing/blank credential rather than discovering it
        // as a 401 on the first call. Runs during service configuration, before the host is built.
        settings.Validate();

        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.ApiKey)
                     && !string.IsNullOrWhiteSpace(s.Subdomain)
                     && !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle must all be configured.")
            .ValidateOnStart();

        // The SDK's DI extension builds the options once at registration and holds a long-lived singleton
        // client over an IHttpClientFactory-managed HttpClient (a rotated secret needs a restart).
        services.AddMaxioAdvancedBillingClient(options =>
        {
            // Sandbox and production Chargify sites are the US environment; Basic auth is accepted there.
            options.Environment = ServerEnvironment.Us;

            // Basic auth: username = Chargify API key, password = literal "x".
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Explicit override: use the configured base URL verbatim.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                // Derive https://{site}.chargify.com from the subdomain.
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            // Per-attempt timeout; the whole-call budget is enforced by a CancellationToken in the service.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
