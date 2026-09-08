using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>();
        return services;
    }
}
