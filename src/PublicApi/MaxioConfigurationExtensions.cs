using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section
    /// without writing secret values into any repository file.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        Map(values, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(values, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(values, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(values, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder;
    }

    private static void Map(Dictionary<string, string?> values, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configurationKey] = value;
        }
    }
}
