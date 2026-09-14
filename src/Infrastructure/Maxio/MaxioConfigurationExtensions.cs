using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Lets the deployment supply Maxio settings through the flat environment variables Maxio hands out,
/// without giving up the <c>Maxio:</c> configuration section as the binding surface.
/// </summary>
public static class MaxioConfigurationExtensions
{
    private static readonly (string EnvironmentVariable, string ConfigurationKey)[] _mappings =
    {
        ("MAXIO_API_KEY", $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.ApiKey)}"),
        ("MAXIO_SITE_SUBDOMAIN", $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.Subdomain)}"),
        ("MAXIO_DEFAULT_PRODUCT_FAMILY", $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}"),
        ("MAXIO_BASE_URL", $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.BaseUrl)}")
    };

    /// <summary>
    /// Seeds <c>Maxio:*</c> from <c>MAXIO_*</c> environment variables at the lowest precedence, so
    /// user secrets, appsettings and explicitly namespaced environment variables still win. Only the
    /// mapping is defined here - the values are read from the environment at start-up and never
    /// stored in the repository.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariableDefaults(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (environmentVariable, configurationKey) in _mappings)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[configurationKey] = value;
            }
        }

        if (values.Count == 0)
        {
            return builder;
        }

        var source = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
        {
            InitialData = values
        };

        // Insert first so every other configuration source overrides these fallbacks.
        builder.Sources.Insert(0, source);
        return builder;
    }
}
