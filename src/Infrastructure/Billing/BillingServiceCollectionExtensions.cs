using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio-backed subscription billing, bound from the "Maxio" configuration section.
    /// The SDK client is built lazily on first use, so missing settings fail billing requests only.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));
        services.AddHttpClient(nameof(MaxioAdvancedBillingClient));
        services.AddSingleton(sp => MaxioClientFactory.Create(
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MaxioAdvancedBillingClient))));
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
