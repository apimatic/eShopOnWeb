using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maps the sandbox-provided Maxio environment variables into the <c>Maxio</c> configuration
/// section so the rest of the app binds settings through <see cref="MaxioOptions"/> only.
/// The variable names are referenced here; their values are never written to any file.
/// </summary>
public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironment(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>
        {
            [MaxioOptions.SectionName + ":ApiKey"] = Environment.GetEnvironmentVariable("MAXIO_API_KEY"),
            [MaxioOptions.SectionName + ":Subdomain"] = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
            [MaxioOptions.SectionName + ":ProductFamilyHandle"] = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY")
        };

        return configuration.AddInMemoryCollection(values);
    }
}
