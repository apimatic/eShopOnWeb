using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: settings bound from the "Maxio"
    /// configuration section, a typed HTTP client, and the subscription domain service.
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
