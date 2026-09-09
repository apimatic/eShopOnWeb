using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: options bound from the
    /// "Maxio" configuration section (values come from user-secrets /
    /// environment variables, never from the repository), the typed API client,
    /// and the subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain) || !string.IsNullOrWhiteSpace(o.BaseUrl),
                "Either Maxio:Subdomain or Maxio:BaseUrl must be configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.");

        services.AddHttpClient<MaxioClient>();
        services.AddSingleton<MaxioUserLockRegistry>();
        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
