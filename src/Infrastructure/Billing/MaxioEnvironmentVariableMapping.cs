using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maps the sandbox/runtime environment variable names onto the <c>Maxio:</c> configuration section.
/// </summary>
public static class MaxioEnvironmentVariableMapping
{
    public static IReadOnlyDictionary<string, string?> GetNonEmptyMappings()
    {
        var mapped = new Dictionary<string, string?>();
        Map(mapped, "MAXIO_API_KEY", "Maxio:ApiKey");
        Map(mapped, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map(mapped, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map(mapped, "MAXIO_BASE_URL", "Maxio:BaseUrl");
        return mapped;
    }

    private static void Map(IDictionary<string, string?> target, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[configurationKey] = value;
        }
    }
}
