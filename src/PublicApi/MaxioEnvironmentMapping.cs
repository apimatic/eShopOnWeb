using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioEnvironmentMapping
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section
    /// without writing secret values to any file.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentMapping(this IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        AddIfPresent(map, "MAXIO_API_KEY", "Maxio:ApiKey");
        AddIfPresent(map, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        AddIfPresent(map, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }

        return builder;
    }

    private static void AddIfPresent(IDictionary<string, string?> map, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }
}
