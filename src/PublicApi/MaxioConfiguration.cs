using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps MAXIO_* environment variables onto the <c>Maxio:</c> configuration section
/// so options bind from those keys regardless of how secrets were supplied.
/// </summary>
public static class MaxioConfiguration
{
    public static void AddEnvironmentOverrides(IConfigurationBuilder configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Map(overrides, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(overrides, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(overrides, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static void Map(Dictionary<string, string?> overrides, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[configurationKey] = value;
        }
    }
}
