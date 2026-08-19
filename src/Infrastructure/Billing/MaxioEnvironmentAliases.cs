using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioEnvironmentAliases
{
    public static IEnumerable<KeyValuePair<string, string?>> FromProcessEnvironment()
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        Add(pairs, "MAXIO_API_KEY", "Maxio:ApiKey");
        Add(pairs, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Add(pairs, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Add(pairs, "MAXIO_BASE_URL", "Maxio:BaseUrl");
        Add(pairs, "MAXIO_ENVIRONMENT", "Maxio:Environment");
        return pairs;
    }

    private static void Add(List<KeyValuePair<string, string?>> pairs, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(value))
        {
            pairs.Add(new KeyValuePair<string, string?>(configKey, value));
        }
    }
}
