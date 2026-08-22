using System;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class PayPalConfiguration
{
    public static void BindEnvironmentVariables(ConfigurationManager configuration)
    {
        Overlay(configuration, "PAYPAL_CLIENT_ID", $"{PayPalOptions.SectionName}:ClientId");
        Overlay(configuration, "PAYPAL_CLIENT_SECRET", $"{PayPalOptions.SectionName}:ClientSecret");
        Overlay(configuration, "PAYPAL_ENVIRONMENT", $"{PayPalOptions.SectionName}:Environment");
        Overlay(configuration, "PAYPAL_CURRENCY", $"{PayPalOptions.SectionName}:Currency");
        Overlay(configuration, "PAYPAL_BASE_URL", $"{PayPalOptions.SectionName}:BaseUrl");
    }

    private static void Overlay(ConfigurationManager configuration, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
