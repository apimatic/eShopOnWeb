using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps the <c>MAXIO_*</c> environment variables onto the <c>Maxio:*</c> configuration keys.
/// </summary>
/// <remarks>
/// Hosting platforms hand these credentials over under their own names. Translating them here means a
/// deployment needs no <c>Maxio__*</c> aliases and no secrets in <c>appsettings*.json</c>, while the
/// application keeps binding a single, conventional configuration section. Standard configuration
/// precedence still applies: anything registered after this call, such as a <c>Maxio__ApiKey</c>
/// environment variable, overrides it.
/// </remarks>
public static class MaxioConfigurationExtensions
{
    private static readonly (string Variable, string Key)[] Mappings =
    {
        ("MAXIO_API_KEY", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}"),
        ("MAXIO_SITE_SUBDOMAIN", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}"),
        ("MAXIO_DEFAULT_PRODUCT_FAMILY", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}"),
        ("MAXIO_BASE_URL", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)}"),
        ("MAXIO_ENVIRONMENT", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Environment)}")
    };

    /// <summary>
    /// Adds any <c>MAXIO_*</c> environment variables that are set as <c>Maxio:*</c> configuration
    /// values. Variables that are absent or blank are ignored, so they never blank out a value that
    /// user-secrets or <c>appsettings.json</c> already supplied.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, key) in Mappings)
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
