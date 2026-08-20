using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalConfigurationExtensions
{
    public static IConfigurationBuilder AddPayPalEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>();
        Overlay(overlay, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Overlay(overlay, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Overlay(overlay, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Overlay(overlay, "PAYPAL_CURRENCY", "PayPal:Currency");
        Overlay(overlay, "PAYPAL_BASE_URL", "PayPal:BaseUrl");
        return builder.AddInMemoryCollection(overlay);
    }

    private static void Overlay(IDictionary<string, string?> overlay, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
