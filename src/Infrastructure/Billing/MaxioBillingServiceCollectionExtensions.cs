using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Registers the Maxio Advanced Billing integration: binds <see cref="MaxioSettings"/> from the
/// <c>Maxio:</c> configuration section, exposes a single long-lived <see cref="MaxioAdvancedBillingClient"/>,
/// and wires the <see cref="ISubscriptionBillingService"/> adapter.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // One client for the app's lifetime. The factory performs no I/O and never throws on missing
        // configuration, so resolving it during DI validation / startup is safe.
        services.AddSingleton(sp =>
            MaxioClientFactory.Create(sp.GetRequiredService<IOptions<MaxioSettings>>().Value));

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
