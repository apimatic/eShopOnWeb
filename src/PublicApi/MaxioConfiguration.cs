using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Lets the host run from the MAXIO_* variables the platform provides. Keys already set by another source
/// (user-secrets, <c>Maxio__*</c> environment variables, test configuration) take precedence.
/// </summary>
public static class MaxioConfiguration
{
    private static readonly (string Key, string Variable)[] Fallbacks =
    {
        ("Maxio:ApiKey", "MAXIO_API_KEY"),
        ("Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN"),
        ("Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY")
    };

    public static void AddMaxioEnvironmentFallback(this ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (key, variable) in Fallbacks)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(configuration[key]) && !string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }
    }
}
