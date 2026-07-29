using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AdvancedBillingClient = AdvancedBilling.Standard.AdvancedBillingClient;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed subscription billing capability: binds <see cref="MaxioSettings"/> from the
    /// <c>Maxio</c> configuration section, provides a singleton <see cref="AdvancedBillingClient"/>, and wires
    /// the <see cref="ISubscriptionBillingService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(options =>
            configuration.GetSection(MaxioSettings.ConfigurationSection).Bind(options));

        // The client is thread-safe and reuses a single HttpClient; build it lazily so an unrelated part of
        // the app still starts even if Maxio settings are absent (the error surfaces only when billing is used).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            return MaxioClientFactory.Create(settings);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
