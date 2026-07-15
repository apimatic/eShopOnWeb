using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

/// <summary>
/// The single composition-root entry point for wiring up the Maxio billing integration (plan.md §2.2:
/// "the provider is touched in exactly one class in Infrastructure, behind one ApplicationCore
/// interface" — this extension is the only place either host needs to reference to get there).
/// </summary>
public static class MaxioBillingClientServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBillingClient(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(maxioSection);
        var maxioSettings = maxioSection.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options => maxioSettings.Configure(options));

        services.AddScoped<IBillingClient, MaxioBillingClient>();

        return services;
    }
}
