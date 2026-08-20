using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps the sandbox credential environment variables onto the Maxio: configuration section.
/// </summary>
internal static class MaxioConfiguration
{
    internal static void ApplyEnvironmentOverrides(IConfigurationBuilder builder)
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
    }

    private static void TryMap(string environmentVariable, string configurationKey, IDictionary<string, string?> overrides)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[configurationKey] = value;
        }
    }
}
