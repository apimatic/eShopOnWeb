using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: settings bound from the "Maxio" configuration
    /// section, a typed HttpClient for <see cref="IMaxioClient"/>, and the <see cref="ISubscriptionBillingService"/>.
    /// Settings are validated when the client is first used, so the host can still start (and serve every
    /// non-billing endpoint) when the integration is not configured.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IMaxioClient, MaxioClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
