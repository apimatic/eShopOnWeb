using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the Maxio sandbox environment variables onto the strongly-typed
/// <c>Maxio:</c> configuration keys, so the same build can target a different site
/// purely by changing environment variables. Values are read from the environment
/// at runtime and are never stored in the repository.
/// </summary>
public static class MaxioEnvironmentConfiguration
{
    // Environment variable name -> "Maxio:" configuration key.
    private static readonly (string EnvVar, string ConfigKey)[] Mappings =
    {
        ("MAXIO_API_KEY", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}"),
        ("MAXIO_SITE_SUBDOMAIN", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}"),
        ("MAXIO_DEFAULT_PRODUCT_FAMILY", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}"),
        // Optional explicit base URL override.
        ("MAXIO_BASE_URL", $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)}"),
    };

    /// <summary>
    /// Adds an in-memory configuration source populated from any present Maxio
    /// environment variables. Only variables that are actually set are added, so
    /// this never clobbers values already provided via user-secrets or appsettings.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var (envVar, configKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides[configKey] = value;
            }
        }

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }

        return configuration;
    }
}
