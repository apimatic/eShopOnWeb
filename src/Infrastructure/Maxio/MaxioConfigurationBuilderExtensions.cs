using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps the well-known MAXIO_* environment variables onto the "Maxio"
/// configuration section. Only variable names appear here — their values
/// are read from the environment / user-secrets at runtime and are never
/// committed to the repository.
/// </summary>
public static class MaxioConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var mapped = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = System.Environment.GetEnvironmentVariable("MAXIO_API_KEY"),
            ["Maxio:Subdomain"] = System.Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
            ["Maxio:Environment"] = System.Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT"),
            ["Maxio:ProductFamilyHandle"] = System.Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"),
        };

        var settings = new Dictionary<string, string?>();
        foreach (var (key, value) in mapped)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                settings[key] = value;
            }
        }

        return builder.AddInMemoryCollection(settings);
    }
}
