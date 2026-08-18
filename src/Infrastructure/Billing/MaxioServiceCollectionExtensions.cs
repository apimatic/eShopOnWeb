using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.ApiKey) ||
                !string.IsNullOrWhiteSpace(options.Subdomain) ||
                !string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = options.ResolveBaseAddress();
            }
        });

        services.AddScoped<ISubscriptionBillingService>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new SubscriptionBillingService(
                provider.GetRequiredService<IMaxioAdvancedBillingClient>(),
                provider.GetRequiredService<IAppLogger<SubscriptionBillingService>>(),
                options.ProductFamilyHandle);
        });

        return services;
    }
}
