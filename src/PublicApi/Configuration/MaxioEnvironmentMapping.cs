using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class MaxioEnvironmentMapping
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
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

    private static void Map(IDictionary<string, string?> mapped, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mapped[configurationKey] = value;
        }
    }
}
