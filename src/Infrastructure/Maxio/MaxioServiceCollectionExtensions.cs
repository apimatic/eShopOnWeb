using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing SDK client and the subscription service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" configuration section, registers the SDK client (over an
    /// <see cref="System.Net.Http.IHttpClientFactory"/>-managed HttpClient) and the
    /// <see cref="IMaxioSubscriptionService"/>.
    /// </summary>
    /// <remarks>
    /// Registration is intentionally tolerant of missing credentials so the host still
    /// starts (e.g. in tests/CI without sandbox secrets). Whether Maxio is actually usable
    /// is validated at call time via <see cref="MaxioSettings.IsConfigured"/>, which yields
    /// a clean 503 instead of an obscure transport failure.
    /// </remarks>
    public static IServiceCollection AddMaxioIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.ConfigurationSectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        // The generated APIMatic DI extension wires an IHttpClientFactory-managed HttpClient
        // and captures these options once, at registration time.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;
            options.BasicAuth = new BasicAuthCredentials
            {
                // Basic auth: Username = API key, Password = the literal "x".
                Username = settings.ApiKey ?? string.Empty,
                Password = "x"
            };

            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            // When an explicit base URL is supplied, use it verbatim instead of deriving one
            // from the subdomain.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
