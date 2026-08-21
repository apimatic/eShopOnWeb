using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioEnvironmentMapping
{
    public static void AddMaxioEnvironmentOverrides(this IConfigurationBuilder configuration)
    {
        var mapped = new Dictionary<string, string?>();
        Copy("MAXIO_API_KEY", "Maxio:ApiKey", mapped);
        Copy("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain", mapped);
        Copy("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle", mapped);
        Copy("MAXIO_BASE_URL", "Maxio:BaseUrl", mapped);
        Copy("MAXIO_ENVIRONMENT", "Maxio:Environment", mapped);

        if (mapped.Count > 0)
        {
            configuration.AddInMemoryCollection(mapped);
        }
    }

    private static void Copy(string environmentVariable, string configurationKey, Dictionary<string, string?> mapped)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mapped[configurationKey] = value;
        }
    }
}
