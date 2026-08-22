using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PayPalConfiguration
{
    public static void BindFromEnvironment(ConfigurationManager configuration)
    {
        var mapped = new Dictionary<string, string?>();
        Map("PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Map("PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Map("PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Map("PAYPAL_CURRENCY", "PayPal:Currency");
        Map("PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (mapped.Count > 0)
        {
            configuration.AddInMemoryCollection(mapped);
        }

        void Map(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                mapped[configurationKey] = value;
            }
        }
    }
}
