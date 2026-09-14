using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maxio hands its credentials out as flat <c>MAXIO_*</c> environment variables, which do not follow
    /// the <c>Section__Key</c> convention .NET binds from. This projects them onto the <c>Maxio</c>
    /// configuration section so a container or CI job can supply them as-is, while local development can
    /// keep using user-secrets. Only variables that are actually set contribute anything.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> VariableMap = new Dictionary<string, string>
    {
        ["MAXIO_API_KEY"] = $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}",
        ["MAXIO_SITE_SUBDOMAIN"] = $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}",
        ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}",
        ["MAXIO_ENVIRONMENT"] = $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Environment)}",
        ["MAXIO_BASE_URL"] = $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)}",
    };

    /// <inheritdoc cref="VariableMap"/>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();

        foreach (var (variable, key) in VariableMap)
        {
            var value = Environment.GetEnvironmentVariable(variable);

            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }
}
