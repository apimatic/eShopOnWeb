using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var mappings = new Dictionary<string, string?>();
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (mappings.Count > 0)
        {
            builder.AddInMemoryCollection(mappings);
        }

        return builder;

        void Map(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                mappings[configurationKey] = value;
            }
        }
    }

    public static IServiceCollection AddMaxioConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        return services;
    }
}
