using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class FinixEnvironmentConfiguration
{
    public static void AddEnvironmentOverrides(IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();

        Add(values, "Username", "FINIX_USERNAME");
        Add(values, "Password", "FINIX_PASSWORD");
        Add(values, "MerchantId", "FINIX_MERCHANT_ID");
        Add(values, "Environment", "FINIX_ENVIRONMENT");
        Add(values, "Currency", "FINIX_CURRENCY");

        builder.AddInMemoryCollection(values);
    }

    private static void Add(IDictionary<string, string?> values, string key, string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
            values[$"{FinixOptions.SectionName}:{key}"] = value;
    }
}
