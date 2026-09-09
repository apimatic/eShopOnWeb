using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: options bound from the "Maxio"
    /// configuration section (values provided via user-secrets or environment variables)
    /// and the HTTP client + subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Maxio:ApiKey is required (set it from MAXIO_API_KEY via user-secrets or an environment variable).")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required (set it from MAXIO_DEFAULT_PRODUCT_FAMILY via user-secrets or an environment variable).")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Subdomain) || !string.IsNullOrWhiteSpace(options.BaseUrl),
                "Either Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl must be set.")
            .ValidateOnStart();

        services.AddHttpClient<MaxioApiClient>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
