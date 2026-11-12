using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio SDK client (built once, so a rotated key needs a restart) and the billing service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = MaxioSettings.Load(configuration);
        services.AddSingleton(settings);
        services.AddSingleton(_ => CreateClient(settings));
        services.AddSingleton<MaxioBillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClient CreateClient(MaxioSettings settings)
    {
        var isEu = string.Equals(settings.Environment!.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
        };
        options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // used verbatim, instead of deriving an address from the subdomain
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        return new MaxioAdvancedBillingClient(new HttpClient(), options);
    }
}
