using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Registers the Maxio Advanced Billing integration. Shared by both hosts (Web and PublicApi;
/// see §4.3) so the base-URL/region resolution logic lives in exactly one place.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBillingServices(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioConfigSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(maxioConfigSection);

        // Read synchronously at registration time (mirrors the existing CatalogSettings pattern in
        // this repo) since the SDK's own AddMaxioAdvancedBillingClient configure delegate has no
        // service-provider access to resolve IOptions<MaxioSettings> lazily.
        var maxioSettings = maxioConfigSection.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options => maxioSettings.ConfigureClientOptions(options));
        services.AddScoped<IBillingClient, MaxioBillingClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
