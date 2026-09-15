using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioServiceCollectionExtensions
{
    public static IConfigurationBuilder AddMaxioFromEnvironment(this IConfigurationBuilder builder)
    {
        var mapped = new Dictionary<string, string?>();
        Map(mapped, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(mapped, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(mapped, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(mapped, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (mapped.Count > 0)
        {
            builder.AddInMemoryCollection(mapped);
        }

        return builder;
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    private static void Map(IDictionary<string, string?> mapped, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mapped[configurationKey] = value;
        }
    }
}
