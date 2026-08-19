using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps the well-known <c>MAXIO_*</c> environment variables onto the <c>Maxio:</c>
    /// configuration section. Values are read from the process environment only — they are
    /// never written to files.
    /// </summary>
    public static IConfigurationBuilder AddMaxioFromEnvironment(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Map(data, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(data, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(data, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(data, "MAXIO_BASE_URL", "Maxio:BaseUrl");
        return builder.AddInMemoryCollection(data);
    }

    private static void Map(IDictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
