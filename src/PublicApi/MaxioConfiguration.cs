using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfiguration
{
    /// <summary>
    /// Maps the well-known <c>MAXIO_*</c> environment variables onto the <c>Maxio:</c> configuration section.
    /// User-secrets and <c>Maxio__*</c> env vars still work; these names are how credentials arrive in this environment.
    /// </summary>
    public static void OverlayEnvironmentVariables(IConfigurationBuilder configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Overlay(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Overlay(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Overlay(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Overlay(overlay, "MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void Overlay(IDictionary<string, string?> overlay, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
