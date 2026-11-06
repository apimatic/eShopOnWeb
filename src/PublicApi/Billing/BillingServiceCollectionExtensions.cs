using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));
        services.AddHttpClient();
        services.AddSingleton<MaxioClientProvider>();
        services.AddScoped<BillingService>();
        return services;
    }
}
