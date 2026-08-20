using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfiguration
{
    public static void ApplyEnvironmentVariables(ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Bind("MAXIO_API_KEY", "Maxio:ApiKey");
        Bind("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Bind("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Bind("MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }

        void Bind(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides[configurationKey] = value;
            }
        }
    }
}
