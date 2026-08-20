using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class PayPalConfiguration
{
    public static void ApplyEnvironmentVariables(IConfigurationBuilder configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Map(overlay, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Map(overlay, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Map(overlay, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Map(overlay, "PAYPAL_CURRENCY", "PayPal:Currency");
        Map(overlay, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void Map(IDictionary<string, string?> overlay, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
