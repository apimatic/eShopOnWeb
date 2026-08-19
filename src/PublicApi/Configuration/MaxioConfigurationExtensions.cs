using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Maps <c>MAXIO_*</c> environment variables onto the <c>Maxio:</c> configuration section.
/// </summary>
public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentBindings(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Copy(overlay, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(overlay, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(overlay, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        var baseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            overlay["Maxio:BaseUrl"] = baseUrl;
        }

        return overlay.Count == 0 ? builder : builder.AddInMemoryCollection(overlay);
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
