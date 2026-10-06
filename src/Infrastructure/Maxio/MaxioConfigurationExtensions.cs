using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps the MAXIO_* environment variables into the "Maxio" configuration section so the same build
/// can run against any Maxio site/catalog by setting environment variables alone.
/// </summary>
public static class MaxioConfigurationExtensions
{
    private static readonly IReadOnlyDictionary<string, string> EnvVarMappings = new Dictionary<string, string>
    {
        ["MAXIO_API_KEY"] = $"{MaxioOptions.SectionName}:ApiKey",
        ["MAXIO_SITE_SUBDOMAIN"] = $"{MaxioOptions.SectionName}:Subdomain",
        ["MAXIO_ENVIRONMENT"] = $"{MaxioOptions.SectionName}:Environment",
        ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = $"{MaxioOptions.SectionName}:ProductFamilyHandle"
    };

    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var mapping in EnvVarMappings)
        {
            string? value = Environment.GetEnvironmentVariable(mapping.Key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[mapping.Value] = value;
            }
        }

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder;
    }
}
