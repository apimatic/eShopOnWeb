using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfiguration
{
    public static void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        SetIfPresent(configuration, "Maxio:ApiKey", "MAXIO_API_KEY");
        SetIfPresent(configuration, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        SetIfPresent(configuration, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        SetIfPresent(configuration, "Maxio:BaseUrl", "MAXIO_BASE_URL");
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            if (!options.IsConfigured)
            {
                return;
            }

            http.BaseAddress = new Uri(options.ResolveApiBaseUrl());
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }

    private static void SetIfPresent(IConfiguration configuration, string configKey, string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configKey] = value;
        }
    }
}
