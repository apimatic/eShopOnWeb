using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioConfiguration
{
    public static void Apply(IConfiguration configuration)
    {
        Copy(configuration, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(configuration, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(configuration, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Require(configuration, "Maxio:ApiKey");
        Require(configuration, "Maxio:Subdomain");
        Require(configuration, "Maxio:ProductFamilyHandle");
    }

    private static void Copy(IConfiguration configuration, string environmentVariable, string key)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
            configuration[key] = value;
    }

    private static void Require(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            throw new InvalidOperationException($"{key} is not configured.");
    }
}
