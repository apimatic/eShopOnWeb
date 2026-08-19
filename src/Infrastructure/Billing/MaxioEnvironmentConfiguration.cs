using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioEnvironmentConfiguration
{
    public static IConfigurationBuilder AddMaxioFromEnvironment(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>();
        Map(data, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(data, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(data, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(data, "MAXIO_ENVIRONMENT", "Maxio:Environment");

        if (data.Count == 0)
        {
            return builder;
        }

        return builder.AddInMemoryCollection(data);
    }

    private static void Map(Dictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
