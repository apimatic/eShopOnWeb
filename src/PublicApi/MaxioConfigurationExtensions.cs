using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps the machine-provided MAXIO_* environment variables onto the Maxio:
/// configuration section without writing secret values into the repository.
/// </summary>
internal static class MaxioConfigurationExtensions
{
    public static void AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>();
        Copy(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        var baseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            overlay["Maxio:BaseUrl"] = baseUrl;
        }

        if (overlay.Count > 0)
        {
            builder.AddInMemoryCollection(overlay);
        }
    }

    private static void Copy(IDictionary<string, string?> overlay, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configKey] = value;
        }
    }
}
