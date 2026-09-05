using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription-billing service, bound
    /// from the "Maxio" configuration section. Subdomain/ApiKey/ProductFamilyHandle/BaseUrl come
    /// from configuration (user-secrets in Development) - nothing here is catalog- or site-specific.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.ConfigSectionName);
        services.Configure<MaxioOptions>(section);
        var options = section.Get<MaxioOptions>() ?? new MaxioOptions();

        services.AddMaxioAdvancedBillingClient(clientOptions =>
        {
            clientOptions.Environment = ServerEnvironment.Us;
            clientOptions.Server.Production.Us.Site = options.Subdomain;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }

            clientOptions.BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey,
                Password = "x"
            };

            // Default per-attempt timeout (100s) is far too long for a request-path call.
            clientOptions.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
