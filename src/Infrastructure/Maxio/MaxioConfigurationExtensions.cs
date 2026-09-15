using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(values, "MAXIO_API_KEY", $"{MaxioOptions.SectionName}:ApiKey");
        AddIfPresent(values, "MAXIO_SITE_SUBDOMAIN", $"{MaxioOptions.SectionName}:Subdomain");
        AddIfPresent(values, "MAXIO_DEFAULT_PRODUCT_FAMILY", $"{MaxioOptions.SectionName}:ProductFamilyHandle");
        AddIfPresent(values, "MAXIO_BASE_URL", $"{MaxioOptions.SectionName}:BaseUrl");

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }

    private static void AddIfPresent(IDictionary<string, string?> values, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configurationKey] = value;
        }
    }
}
