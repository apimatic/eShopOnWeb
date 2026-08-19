using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioConfiguration
{
    /// <summary>
    /// Maps the sandbox environment-variable names onto the <c>Maxio:</c> configuration section.
    /// </summary>
    public static void ApplyEnvironmentOverrides(IConfigurationManager configuration)
    {
        Copy(configuration, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(configuration, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(configuration, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy(configuration, "MAXIO_ENVIRONMENT", "Maxio:Environment");
        Copy(configuration, "MAXIO_BASE_URL", "Maxio:BaseUrl");
    }

    private static void Copy(IConfigurationManager configuration, string environmentKey, string configurationKey)
    {
        var value = configuration[environmentKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable(environmentKey);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
