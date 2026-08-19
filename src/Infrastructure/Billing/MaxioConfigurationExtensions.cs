using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioConfigurationExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>();
        Map(data, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(data, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(data, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (data.Count == 0)
        {
            return builder;
        }

        return builder.AddInMemoryCollection(data);
    }

    private static void Map(IDictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
