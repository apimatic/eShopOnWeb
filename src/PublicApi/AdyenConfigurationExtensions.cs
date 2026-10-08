using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class AdyenConfigurationExtensions
{
    // Deployment environment variable → configuration key bound into AdyenSettings.
    private static readonly IReadOnlyDictionary<string, string> AdyenVariables = new Dictionary<string, string>
    {
        ["ADYEN_API_KEY"] = "Adyen:ApiKey",
        ["ADYEN_MERCHANT_ACCOUNT"] = "Adyen:MerchantAccount",
        ["ADYEN_ENVIRONMENT"] = "Adyen:Environment",
        ["ADYEN_CURRENCY"] = "Adyen:Currency",
    };

    /// <summary>
    /// Maps the ADYEN_* environment variables onto the Adyen:* keys. Only variables that are set are added, so
    /// values from user-secrets or a secret store remain in effect when a variable is absent.
    /// </summary>
    public static IConfigurationBuilder AddAdyenEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in AdyenVariables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }
}
