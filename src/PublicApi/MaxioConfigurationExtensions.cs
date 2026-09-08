using System;
using Microsoft.Extensions.Configuration;
using Microsoft.eShopWeb.Maxio.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Loads the Maxio credentials/catalog from their MAXIO_* environment variables into the
/// "Maxio" configuration section so the rest of the application can bind them with the
/// standard Maxio:* keys. Environment variables that are not present are left untouched,
/// which lets operators provide the values through user secrets or Maxio__* variables instead.
/// </summary>
public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        if (configuration is IConfigurationManager configurationManager)
        {
            Apply(configurationManager, "MAXIO_API_KEY", nameof(MaxioOptions.ApiKey));
            Apply(configurationManager, "MAXIO_SITE_SUBDOMAIN", nameof(MaxioOptions.Subdomain));
            Apply(configurationManager, "MAXIO_DEFAULT_PRODUCT_FAMILY", nameof(MaxioOptions.ProductFamilyHandle));
            Apply(configurationManager, "MAXIO_ENVIRONMENT", nameof(MaxioOptions.Environment));
        }

        return configuration;
    }

    private static void Apply(IConfigurationManager configurationManager, string environmentVariableName, string key)
    {
        string? value = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrEmpty(value))
        {
            configurationManager[$"{MaxioOptions.SectionName}:{key}"] = value;
        }
    }
}
