using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration.
    /// Settings are bound from the "Maxio" configuration section; their values are supplied
    /// through user-secrets / environment variables and are never hard-coded.
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => o.GetMissingRequiredKeys().Count == 0,
                "Maxio configuration is incomplete. Required keys: Maxio:ApiKey, Maxio:ProductFamilyHandle and " +
                "either Maxio:Subdomain or Maxio:BaseUrl. Supply them via user-secrets or environment variables " +
                "(MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY).")
            .ValidateOnStart();

        services.AddHttpClient<MaxioClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IMaxioClient>(sp => sp.GetRequiredService<MaxioClient>());
        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
