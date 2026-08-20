using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfiguration
{
    public static void ApplyEnvironmentOverrides(ConfigurationManager configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Add(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Add(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Add(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Add(overlay, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void Add(IDictionary<string, string?> overlay, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configKey] = value;
        }
    }
}
