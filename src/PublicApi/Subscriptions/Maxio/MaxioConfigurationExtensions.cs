using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>();
        MapIfPresent(values, "MAXIO_API_KEY", "Maxio:ApiKey");
        MapIfPresent(values, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        MapIfPresent(values, "MAXIO_ENVIRONMENT", "Maxio:Environment");
        MapIfPresent(values, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        MapIfPresent(values, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        return values.Count > 0 ? configuration.AddInMemoryCollection(values) : configuration;
    }

    private static void MapIfPresent(IDictionary<string, string?> values, string environmentVariable, string configurationKey)
    {
        string? value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configurationKey] = value;
        }
    }
}
