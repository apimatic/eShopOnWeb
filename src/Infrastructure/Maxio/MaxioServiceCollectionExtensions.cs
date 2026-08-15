using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Maxio:</c> configuration section, registers the Maxio Advanced Billing SDK client
    /// (Basic auth; base address from <c>Maxio:BaseUrl</c> when set, otherwise derived from
    /// <c>Maxio:Subdomain</c>), and wires <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();
        ValidateSettings(settings);

        services.AddMaxioAdvancedBillingClient(options =>
        {
            // Basic auth: username = API key, password = the literal "x".
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            };

            options.Environment = ServerEnvironment.Us;

            if (settings.HasBaseUrlOverride)
            {
                // Use the configured base address verbatim.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                // Derive the base address from the site subdomain (https://{subdomain}.chargify.com).
                options.Server.Production.Us.Site = settings.Subdomain;
            }
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private static void ValidateSettings(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or the environment.");
        }

        if (!settings.HasBaseUrlOverride && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain (or Maxio:BaseUrl) must be configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured.");
        }
    }
}
