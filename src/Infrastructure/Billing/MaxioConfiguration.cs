using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioConfiguration
{
    public static IConfigurationBuilder AddMaxioEnvironmentAliases(this IConfigurationBuilder builder)
    {
        var overrides = new Dictionary<string, string?>();
        TryMap("MAXIO_API_KEY", "Maxio:ApiKey", overrides);
        TryMap("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain", overrides);
        TryMap("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle", overrides);
        TryMap("MAXIO_BASE_URL", "Maxio:BaseUrl", overrides);

        if (overrides.Count > 0)
        {
            builder.AddInMemoryCollection(overrides);
        }

        return builder;
    }

    private static void TryMap(string environmentVariable, string configurationKey, Dictionary<string, string?> overrides)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[configurationKey] = value;
        }
    }
}
