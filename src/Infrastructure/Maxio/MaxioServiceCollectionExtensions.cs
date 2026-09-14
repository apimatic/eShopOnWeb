using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio billing capability: the strongly-typed <see cref="MaxioSettings"/> (bound
/// from the <c>Maxio:</c> section and validated on start), the Maxio SDK client (Basic auth, site
/// subdomain or explicit base-URL override), and the <see cref="IMaxioBillingService"/> facade.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read once at registration to configure the SDK client (the callback runs at build time,
        // so it may read IConfiguration/user-secrets but not scoped services).
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;

            // Basic auth: username = API key, password = the literal "x".
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            };

            // Explicit BaseUrl wins verbatim; otherwise derive the address from the site subdomain.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            else
                options.Server.Production.Us.Site = settings.Subdomain;
        });

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
