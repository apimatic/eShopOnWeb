using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioBillingGateway, MaxioAdvancedBillingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ApiKey) && string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri("https://localhost/");
                return;
            }

            MaxioAdvancedBillingClient.ConfigureHttpClient(client, options);
        });
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
