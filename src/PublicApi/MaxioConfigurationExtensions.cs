using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Adds Maxio Advanced Billing configuration by mapping environment variables
    /// to the Maxio configuration section. Values are never written to files.
    /// </summary>
    public static IConfigurationBuilder AddMaxioConfiguration(this IConfigurationBuilder configuration)
    {
        var envVars = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = Environment.GetEnvironmentVariable("MAXIO_API_KEY"),
            ["Maxio:Subdomain"] = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
            ["Maxio:ProductFamilyHandle"] = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"),
            ["Maxio:BaseUrl"] = Environment.GetEnvironmentVariable("MAXIO_BASE_URL"),
        };

        var nonNullVars = envVars.Where(kvp => !string.IsNullOrEmpty(kvp.Value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (nonNullVars.Count > 0)
        {
            configuration.AddInMemoryCollection(nonNullVars);
        }

        return configuration;
    }
}
