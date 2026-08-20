using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps well-known MAXIO_* environment variable names onto the Maxio: configuration section.
/// Values are never written to files; they overlay configuration in memory.
/// </summary>
internal static class MaxioEnvironmentVariables
{
    public static IConfigurationBuilder AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Copy(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy(overlay, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overlay.Count > 0)
        {
            builder.AddInMemoryCollection(overlay);
        }

        return builder;
    }

    private static void Copy(IDictionary<string, string?> overlay, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
