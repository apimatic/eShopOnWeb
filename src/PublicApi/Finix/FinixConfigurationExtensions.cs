using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public static class FinixConfigurationExtensions
{
    public static void AddFinixEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>();

        AddIfPresent(values, "Finix:Username", "FINIX_USERNAME");
        AddIfPresent(values, "Finix:Password", "FINIX_PASSWORD");
        AddIfPresent(values, "Finix:MerchantId", "FINIX_MERCHANT_ID");
        AddIfPresent(values, "Finix:Environment", "FINIX_ENVIRONMENT");
        AddIfPresent(values, "Finix:Currency", "FINIX_CURRENCY");

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }
    }

    private static void AddIfPresent(IDictionary<string, string?> values, string configurationKey,
        string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configurationKey] = value;
        }
    }
}
