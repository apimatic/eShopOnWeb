using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps MAXIO_* environment variable names onto the Maxio: configuration section.
/// Values are never written to files; only configuration keys are populated in memory.
/// </summary>
public static class MaxioEnvironmentVariables
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>();
        TryMap(data, "Maxio:ApiKey", "MAXIO_API_KEY");
        TryMap(data, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        TryMap(data, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        TryMap(data, "Maxio:BaseUrl", "MAXIO_BASE_URL");

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }

        return builder;
    }

    private static void TryMap(IDictionary<string, string?> data, string configKey, string envName)
    {
        var value = System.Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configKey] = value;
        }
    }
}
