using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(options =>
        {
            var section = configuration.GetSection(MaxioOptions.SectionName);
            options.ApiKey = FirstNonEmpty(section["ApiKey"], configuration["MAXIO_API_KEY"]);
            options.Subdomain = FirstNonEmpty(section["Subdomain"], configuration["MAXIO_SITE_SUBDOMAIN"]);
            options.ProductFamilyHandle = FirstNonEmpty(section["ProductFamilyHandle"], configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"]);
            options.BaseUrl = FirstNonEmpty(section["BaseUrl"], configuration["MAXIO_BASE_URL"]);
        });

        services.AddTransient<LastStatusHandler>();
        services.AddHttpClient(MaxioClientFactory.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<LastStatusHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(MaxioClientFactory.HttpClientName);
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var environmentName = configuration["MAXIO_ENVIRONMENT"];
            return MaxioClientFactory.Create(httpClient, settings, environmentName);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
