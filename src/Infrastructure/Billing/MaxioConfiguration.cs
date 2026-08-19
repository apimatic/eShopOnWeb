using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioConfiguration
{
    /// <summary>
    /// Copies sandbox env vars onto the <c>Maxio:</c> section without writing secret values into files.
    /// </summary>
    public static void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        Assign(configuration, "MAXIO_API_KEY", "Maxio:ApiKey");
        Assign(configuration, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Assign(configuration, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Assign(configuration, "MAXIO_BASE_URL", "Maxio:BaseUrl");
        Assign(configuration, "MAXIO_ENVIRONMENT", "Maxio:Environment");
    }

    private static void Assign(IConfiguration configuration, string envName, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
