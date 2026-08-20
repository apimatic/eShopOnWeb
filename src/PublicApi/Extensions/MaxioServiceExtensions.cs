using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Copies <c>MAXIO_*</c> environment variables onto the <c>Maxio:</c> configuration keys
    /// so the same settings work from user-secrets or the process environment.
    /// </summary>
    public static void AddMaxioEnvironmentAliases(this ConfigurationManager configuration)
    {
        var aliases = new Dictionary<string, string?>();
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map("MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (aliases.Count > 0)
        {
            configuration.AddInMemoryCollection(aliases);
        }

        void Map(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                aliases[configurationKey] = value;
            }
        }
    }
}
