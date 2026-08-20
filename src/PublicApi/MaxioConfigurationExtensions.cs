using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section.
    /// Values are not written to disk.
    /// </summary>
    public static void AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        Copy(map, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(map, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(map, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy(map, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }
    }

    private static void Copy(IDictionary<string, string?> map, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }
}
