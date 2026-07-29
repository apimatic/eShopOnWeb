using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing subscription integration: binds the <c>Maxio:</c>
    /// settings section, the typed <see cref="MaxioApiClient"/> (via IHttpClientFactory), and the
    /// <see cref="ISubscriptionBillingService"/>. Settings are validated lazily on first use, so the
    /// host still boots when billing is not configured (e.g. in tests).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddHttpClient<MaxioApiClient>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
