using System;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var environment = configuration["MAXIO_ENVIRONMENT"];
            client.BaseAddress = settings.IsConfigured
                ? settings.GetApiBaseAddress(environment)
                : new Uri("https://unconfigured.invalid/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
