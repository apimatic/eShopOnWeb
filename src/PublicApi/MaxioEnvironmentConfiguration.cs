using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioEnvironmentConfiguration
{
    public static void AddMaxioEnvironmentOverrides(this IConfigurationBuilder configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Map(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(overlay, "MAXIO_ENVIRONMENT", "Maxio:Environment");
        Map(overlay, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void Map(IDictionary<string, string?> overlay, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configKey] = value;
        }
    }
}
