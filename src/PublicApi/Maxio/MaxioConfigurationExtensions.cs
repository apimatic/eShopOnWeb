using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        AddIfPresent(values, "MAXIO_API_KEY", "Maxio:ApiKey");
        AddIfPresent(values, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        AddIfPresent(values, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder;
    }

    private static void AddIfPresent(Dictionary<string, string?> values, string environmentVariableName, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configurationKey] = value;
        }
    }
}
