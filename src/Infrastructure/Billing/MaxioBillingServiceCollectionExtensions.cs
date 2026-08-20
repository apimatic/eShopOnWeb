using System;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ApplyEnvironmentOverrides(configuration);
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });
        services.AddScoped<ISubscriptionBillingService, ApplicationCore.Services.SubscriptionBillingService>();
        return services;
    }

    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section when present.
    /// Values themselves are never written to disk by this method.
    /// </summary>
    public static void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        Copy("MAXIO_API_KEY", "Maxio:ApiKey");
        Copy("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy("MAXIO_BASE_URL", "Maxio:BaseUrl");

        void Copy(string environmentVariable, string configKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(value) || configuration is not IConfigurationRoot root)
            {
                return;
            }

            root[configKey] = value;
        }
    }
}
