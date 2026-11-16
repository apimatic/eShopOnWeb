using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class FinixEnvironmentConfigurationExtensions
{
    /// <summary>
    /// Maps the Finix sandbox/live environment variables onto the Finix configuration section.
    /// Values are read at runtime and are never written to a repository file.
    /// </summary>
    public static IConfigurationBuilder AddFinixEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        Add(values, "Finix:Username", "FINIX_USERNAME");
        Add(values, "Finix:Password", "FINIX_PASSWORD");
        Add(values, "Finix:MerchantId", "FINIX_MERCHANT_ID");
        Add(values, "Finix:Environment", "FINIX_ENVIRONMENT");
        Add(values, "Finix:Currency", "FINIX_CURRENCY");
        Add(values, "Finix:BaseUrl", "FINIX_BASEURL");
        Add(values, "Finix:BaseUrl", "FINIX_BASE_URL");

        builder.AddInMemoryCollection(values);
        return builder;
    }

    private static void Add(IDictionary<string, string?> values, string key, string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }
}
