using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Registers the subscription feature's provider-agnostic seam and its single Maxio implementation -
/// shared between the Web and PublicApi composition roots (plan.md §4.3), since both hosts already
/// reference Infrastructure. The outbound target server (prod/dev/mock) is resolved entirely inside
/// <see cref="MaxioBillingClient"/> from <see cref="MaxioSettings"/> - never hardcode it in a host.
/// </summary>
public static class SubscriptionBillingServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionBillingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));
        services.AddSingleton<MeteredComponentValidationCache>();
        services.AddHttpClient<IBillingClient, MaxioBillingClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddHostedService<MeteredComponentStartupValidator>();

        return services;
    }
}
