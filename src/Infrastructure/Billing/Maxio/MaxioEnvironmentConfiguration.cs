using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maps the documented <c>MAXIO_*</c> environment variable names onto the <c>Maxio:</c>
/// configuration keys. .NET's default env-var provider does not perform this mapping.
/// </summary>
public static class MaxioEnvironmentConfiguration
{
    public const string ApiKeyVariable = "MAXIO_API_KEY";
    public const string SubdomainVariable = "MAXIO_SITE_SUBDOMAIN";
    public const string ProductFamilyVariable = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    public static void Apply(IConfigurationBuilder configuration)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        Map(data, ApiKeyVariable, "Maxio:ApiKey");
        Map(data, SubdomainVariable, "Maxio:Subdomain");
        Map(data, ProductFamilyVariable, "Maxio:ProductFamilyHandle");

        if (data.Count > 0)
        {
            configuration.AddInMemoryCollection(data);
        }
    }

    private static void Map(IDictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
