using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioConfigurationExtensions
{
    // Maxio:* key ← conventional sandbox environment variable. Only names live here; values come from the environment.
    private static readonly (string Key, string Variable)[] Fallbacks =
    {
        ("Maxio:ApiKey", "MAXIO_API_KEY"),
        ("Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN"),
        ("Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY"),
        ("Maxio:Environment", "MAXIO_ENVIRONMENT"),
    };

    /// <summary>
    /// Fills any <c>Maxio:</c> key that no other source (user-secrets, <c>Maxio__*</c> env vars, …) set from the
    /// MAXIO_* environment variables. Values are held in memory only.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariableFallback(this ConfigurationManager configuration)
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

        return values.Count == 0 ? configuration : configuration.AddInMemoryCollection(values);
    }
}
