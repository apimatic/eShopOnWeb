using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Copies well-known MAXIO_* environment variables onto the Maxio: configuration section.
    /// Values are never written to the repository; this only binds process environment into IConfiguration.
    /// </summary>
    public static IConfigurationBuilder AddMaxioFromEnvironment(this IConfigurationBuilder builder)
    {
        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Map(pairs, "Maxio:ApiKey", "MAXIO_API_KEY");
        Map(pairs, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        Map(pairs, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        Map(pairs, "Maxio:Environment", "MAXIO_ENVIRONMENT");
        Map(pairs, "Maxio:BaseUrl", "MAXIO_BASE_URL");
        return builder.AddInMemoryCollection(pairs);
    }

    private static void Map(IDictionary<string, string?> pairs, string configKey, string envName)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            pairs[configKey] = value;
        }
    }
}
