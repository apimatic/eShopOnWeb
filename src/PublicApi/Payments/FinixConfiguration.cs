using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class FinixConfiguration
{
    private static readonly IReadOnlyDictionary<string, string> EnvironmentKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Finix:Username"] = "FINIX_USERNAME",
            ["Finix:Password"] = "FINIX_PASSWORD",
            ["Finix:MerchantId"] = "FINIX_MERCHANT_ID",
            ["Finix:Environment"] = "FINIX_ENVIRONMENT",
            ["Finix:Currency"] = "FINIX_CURRENCY"
        };

    public static void AddEnvironmentFallbacks(IConfigurationBuilder builder, IConfiguration configuration)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var (configurationKey, environmentKey) in EnvironmentKeys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[configurationKey]))
            {
                continue;
            }

            var value = Environment.GetEnvironmentVariable(environmentKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides[configurationKey] = value;
            }
        }

        if (overrides.Count > 0)
        {
            builder.AddInMemoryCollection(overrides);
        }
    }
}
