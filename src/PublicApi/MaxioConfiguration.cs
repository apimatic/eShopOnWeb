using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps MAXIO_* environment variables onto the Maxio: configuration section.
/// </summary>
public static class MaxioConfiguration
{
    public static void ApplyEnvironmentOverrides(ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Copy(overrides, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(overrides, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(overrides, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy(overrides, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static void Copy(IDictionary<string, string?> target, string envName, string configKey)
    {
        var value = System.Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[configKey] = value;
        }
    }
}
