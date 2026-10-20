using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioEnvironmentConfiguration
{
    private static readonly (string Key, string EnvVar)[] Map =
    {
        ("Maxio:ApiKey", "MAXIO_API_KEY"),
        ("Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN"),
        ("Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY"),
    };

    /// <summary>
    /// Maps the conventional MAXIO_* environment variables onto the Maxio: keys, only where the key is
    /// still unset (user-secrets, appsettings and Maxio__* variables win). Values are never logged.
    /// </summary>
    public static void AddMaxioEnvironmentFallback(this ConfigurationManager configuration)
    {
        var fallback = new Dictionary<string, string?>();
        foreach (var (key, envVar) in Map)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key])) continue;
            var value = System.Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value)) fallback[key] = value;
        }
        if (fallback.Count > 0)
        {
            configuration.AddInMemoryCollection(fallback);
        }
    }
}
