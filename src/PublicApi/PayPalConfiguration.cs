using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PayPalConfiguration
{
    public static void OverlayEnvironmentVariables(IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        Copy(map, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Copy(map, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Copy(map, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Copy(map, "PAYPAL_CURRENCY", "PayPal:Currency");
        Copy(map, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }
    }

    public static PayPalOptions RequireValid(IConfiguration configuration)
    {
        var options = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>() ?? new PayPalOptions();
        Require(options.ClientId, "PayPal:ClientId");
        Require(options.ClientSecret, "PayPal:ClientSecret");
        Require(options.Currency, "PayPal:Currency");
        return options;
    }

    private static void Copy(IDictionary<string, string?> map, string environmentName, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }

    private static void Require(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} is required.");
        }
    }
}
