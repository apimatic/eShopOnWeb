using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps the well-known MAXIO_* environment variable names onto the Maxio: configuration section.
/// </summary>
internal static class MaxioEnvironmentConfiguration
{
    public static void AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        AddIfPresent(map, "MAXIO_API_KEY", "Maxio:ApiKey");
        AddIfPresent(map, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        AddIfPresent(map, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        var existing = builder.Build();
        if (string.IsNullOrWhiteSpace(existing["Maxio:BaseUrl"]) && !map.ContainsKey("Maxio:BaseUrl"))
        {
            var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
            var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN")
                            ?? existing["Maxio:Subdomain"];
            if (string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(subdomain))
            {
                map["Maxio:BaseUrl"] = $"https://{subdomain.Trim()}.ebilling.maxio.com";
            }
        }

        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }
    }

    private static void AddIfPresent(IDictionary<string, string?> map, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configKey] = value;
        }
    }
}
