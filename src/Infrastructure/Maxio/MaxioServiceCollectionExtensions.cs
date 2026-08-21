using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddTransient<MaxioRetryHandler>();
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", MaxioJson.BasicCredential(options.ApiKey));
                }
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }

    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section
    /// so the same keys work in local Development (user-secrets) and hosted environments.
    /// </summary>
    public static IConfiguration AddMaxioEnvironmentVariables(this IConfiguration configuration)
    {
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map("MAXIO_BASE_URL", "Maxio:BaseUrl");
        return configuration;

        void Map(string environmentName, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                configuration[configurationKey] = value;
            }
        }
    }
}
