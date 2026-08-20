using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Copies well-known MAXIO_* environment variables onto the Maxio: configuration section.
    /// Existing Maxio:* values (user-secrets, appsettings) win when already set.
    /// </summary>
    public static void AddMaxioEnvironmentOverrides(this ConfigurationManager configuration)
    {
        var mappings = new Dictionary<string, string?>();
        CopyIfUnset(configuration, mappings, "MAXIO_API_KEY", "Maxio:ApiKey");
        CopyIfUnset(configuration, mappings, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        CopyIfUnset(configuration, mappings, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        CopyIfUnset(configuration, mappings, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (mappings.Count > 0)
        {
            configuration.AddInMemoryCollection(mappings);
        }
    }

    private static void CopyIfUnset(
        IConfiguration configuration,
        IDictionary<string, string?> mappings,
        string environmentName,
        string configurationKey)
    {
        if (!string.IsNullOrWhiteSpace(configuration[configurationKey]))
        {
            return;
        }

        var value = configuration[environmentName];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = System.Environment.GetEnvironmentVariable(environmentName);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            mappings[configurationKey] = value;
        }
    }
}
