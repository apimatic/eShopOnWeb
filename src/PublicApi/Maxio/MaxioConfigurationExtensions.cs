using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps the standard <c>MAXIO_*</c> environment variables onto the <c>Maxio:</c> configuration
    /// section used by the rest of the application. Values are never hard-coded anywhere in the
    /// repository; they are read from the environment (or, in development, from .NET user-secrets).
    /// </summary>
    public static ConfigurationManager AddMaxioEnvironmentConfiguration(this ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        Add(values, "MAXIO_API_KEY", MaxioOptions.SectionName + ":ApiKey");
        Add(values, "MAXIO_SITE_SUBDOMAIN", MaxioOptions.SectionName + ":Subdomain");
        Add(values, "MAXIO_DEFAULT_PRODUCT_FAMILY", MaxioOptions.SectionName + ":ProductFamilyHandle");
        Add(values, "MAXIO_BASE_URL", MaxioOptions.SectionName + ":BaseUrl");

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }

        return configuration;
    }

    private static void Add(IDictionary<string, string?> values, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configurationKey] = value;
        }
    }
}
