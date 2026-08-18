using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps canonical MAXIO_* environment variables onto the Maxio: configuration section.
/// Values are never written to the repository; this only copies them into the configuration tree at runtime.
/// </summary>
public static class MaxioConfiguration
{
    public static void AddFromCanonicalEnvironmentVariables(IConfigurationBuilder configuration)
    {
        var mapped = new Dictionary<string, string?>();
        TryMap("MAXIO_API_KEY", "Maxio:ApiKey");
        TryMap("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        TryMap("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (mapped.Count > 0)
        {
            configuration.AddInMemoryCollection(mapped);
        }

        void TryMap(string environmentVariable, string configurationKey)
        {
            var value = System.Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                mapped[configurationKey] = value;
            }
        }
    }
}
