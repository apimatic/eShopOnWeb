using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioEnvironmentConfiguration
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        Copy(data, "MAXIO_API_KEY", "Maxio:ApiKey");
        Copy(data, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy(data, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        var explicitBaseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL");
        if (!string.IsNullOrWhiteSpace(explicitBaseUrl))
        {
            data["Maxio:BaseUrl"] = explicitBaseUrl;
        }
        else
        {
            var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
            var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
            if (!string.IsNullOrWhiteSpace(subdomain) &&
                string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase))
            {
                data["Maxio:BaseUrl"] = $"https://{subdomain.Trim()}.ebilling.maxio.com";
            }
        }

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }

        return builder;
    }

    private static void Copy(IDictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
