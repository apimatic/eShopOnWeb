using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        services.AddMaxioAdvancedBillingClient(options =>
        {
            var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
            Configure(options, settings);
        });

        services.AddScoped<BillingService>();
        return services;
    }

    public static void Configure(MaxioAdvancedBillingClientOptions options, MaxioSettings settings)
    {
        var eu = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
        options.Environment = eu ? ServerEnvironment.Eu : ServerEnvironment.Us;

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain.Trim();
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim instead of the address derived from the subdomain.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // Maxio uses HTTP basic auth: the API key is the user name, the password is ignored.
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
        }
    }
}
