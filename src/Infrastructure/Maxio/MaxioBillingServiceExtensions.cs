using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing client and the <see cref="ISubscriptionBillingService"/>.
/// Binds the <c>Maxio:</c> configuration section; no credential value is ever hard-coded.
/// </summary>
public static class MaxioBillingServiceExtensions
{
    // Maxio API keys authenticate via HTTP Basic with the key as the username and a fixed literal
    // password of "x"; this is a protocol constant, not a secret.
    private const string ApiKeyPasswordSentinel = "x";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = ApiKeyPasswordSentinel
            };

            // A configured BaseUrl wins and is used verbatim; otherwise derive the address from the
            // site subdomain (→ https://{subdomain}.chargify.com).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }
}
