using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section
    /// so the same build can target a different site without committed secrets.
    /// </summary>
    public static void AddMaxioEnvironmentOverrides(this ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }

        void Map(string environmentVariable, string configurationKey)
        {
            var value = System.Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides[configurationKey] = value;
            }
        }
    }
}
