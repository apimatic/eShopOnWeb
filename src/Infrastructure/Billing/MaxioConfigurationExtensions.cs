using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps the sandbox env vars onto the <c>Maxio:</c> section without writing values into the repo.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var mapped = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Map(mapped, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(mapped, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(mapped, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(mapped, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (mapped.Count > 0)
        {
            builder.AddInMemoryCollection(mapped);
        }

        return builder;
    }

    private static void Map(IDictionary<string, string?> mapped, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mapped[configKey] = value;
        }
    }
}
