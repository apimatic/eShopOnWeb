using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalEnvironmentConfiguration
{
    public static void AddOverrides(IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        Add(map, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Add(map, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Add(map, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Add(map, "PAYPAL_CURRENCY", "PayPal:Currency");
        Add(map, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }
    }

    private static void Add(Dictionary<string, string?> map, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }
}
