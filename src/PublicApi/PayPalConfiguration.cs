using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PayPalConfiguration
{
    public static void BindFromEnvironment(IConfiguration configuration)
    {
        Copy("PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Copy("PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Copy("PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Copy("PAYPAL_CURRENCY", "PayPal:Currency");

        void Copy(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                configuration[configurationKey] = value;
            }
        }
    }
}
