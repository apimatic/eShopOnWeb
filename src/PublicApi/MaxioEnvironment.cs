using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Copies MAXIO_* environment variables onto the Maxio: configuration section
/// without writing secret values into the repository.
/// </summary>
internal static class MaxioEnvironment
{
    public static void Overlay(ConfigurationManager configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Copy(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy(overlay, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
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
