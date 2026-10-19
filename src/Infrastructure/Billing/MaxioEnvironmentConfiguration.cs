using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioEnvironmentConfiguration
{
    public static void Apply(IConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>();
        CopyIfSet(values, "Maxio:ApiKey", "MAXIO_API_KEY");
        CopyIfSet(values, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        CopyIfSet(values, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        if (values.Count == 0)
            return;

        configuration.AddInMemoryCollection(values);
    }

    private static void CopyIfSet(IDictionary<string, string?> values, string key, string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value))
            values[key] = value;
    }
}
