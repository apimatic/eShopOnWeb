using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section.
    /// Values are never written to disk; empty variables are ignored so user-secrets remain usable.
    /// </summary>
    public static ConfigurationManager AddMaxioEnvironmentVariables(this ConfigurationManager configuration)
    {
        var mapped = new Dictionary<string, string?>();
        Map(mapped, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(mapped, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(mapped, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(mapped, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (mapped.Count > 0)
        {
            configuration.AddInMemoryCollection(mapped);
        }

        return configuration;
    }

    private static void Map(IDictionary<string, string?> target, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[configurationKey] = value;
        }
    }
}
