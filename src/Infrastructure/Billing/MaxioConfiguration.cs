using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioConfiguration
{
    public static IEnumerable<KeyValuePair<string, string?>> GetEnvironmentOverrides()
    {
        var overrides = new List<KeyValuePair<string, string?>>();
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map("MAXIO_ENVIRONMENT", "Maxio:Environment");
        return overrides;

        void Map(string envName, string configKey)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides.Add(new KeyValuePair<string, string?>(configKey, value));
            }
        }
    }
}
