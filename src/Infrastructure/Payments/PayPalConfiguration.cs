using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalConfiguration
{
    public static void ApplyEnvironmentVariables(IConfigurationManager configuration)
    {
        var mappings = new Dictionary<string, string?>();
        Map(configuration, mappings, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Map(configuration, mappings, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Map(configuration, mappings, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Map(configuration, mappings, "PAYPAL_CURRENCY", "PayPal:Currency");

        if (mappings.Count > 0)
        {
            configuration.AddInMemoryCollection(mappings);
        }
    }

    private static void Map(IConfiguration configuration, IDictionary<string, string?> mappings, string envName, string key)
    {
        var value = configuration[envName] ?? Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mappings[key] = value;
        }
    }

    public static void AddPayPalBindings(this IConfigurationManager configuration)
    {
        ApplyEnvironmentVariables(configuration);
    }
}
