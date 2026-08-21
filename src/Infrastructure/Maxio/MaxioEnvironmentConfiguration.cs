using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps canonical MAXIO_* environment variable names onto the Maxio: configuration section.
/// Values are read from the process environment and never written to disk.
/// </summary>
public static class MaxioEnvironmentConfiguration
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var overrides = new Dictionary<string, string?>();
        Map(overrides, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(overrides, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(overrides, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(overrides, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overrides.Count > 0)
        {
            builder.AddInMemoryCollection(overrides);
        }

        return builder;
    }

    private static void Map(IDictionary<string, string?> target, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[configurationKey] = value;
        }
    }
}
