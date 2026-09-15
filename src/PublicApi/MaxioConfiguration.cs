using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfiguration
{
    public static void AddMaxioEnvironmentOverrides(this IConfigurationBuilder configuration)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Copy(overrides, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(overrides, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(overrides, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static void Copy(IDictionary<string, string?> target, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[configurationKey] = value;
        }
    }
}
