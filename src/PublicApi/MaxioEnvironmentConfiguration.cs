using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps the Maxio sandbox environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY) onto the canonical "Maxio:" configuration keys.
/// The fallback only fills keys that are otherwise unset, so explicit configuration
/// (appsettings, Maxio__* environment variables, or dotnet user-secrets) always wins.
/// Values are never logged or persisted to the repository.
/// </summary>
public static class MaxioEnvironmentConfiguration
{
    private static readonly (string Key, string EnvironmentVariable)[] KeyToEnvironmentVariable =
    {
        ($"{MaxioSettings.SECTION_NAME}:ApiKey", "MAXIO_API_KEY"),
        ($"{MaxioSettings.SECTION_NAME}:Subdomain", "MAXIO_SITE_SUBDOMAIN"),
        ($"{MaxioSettings.SECTION_NAME}:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY"),
    };

    public static IConfigurationBuilder ApplyMaxioEnvironmentFallback(this IConfigurationBuilder builder)
    {
        // Build a snapshot of what is already configured (this builder runs after the
        // default sources: appsettings, environment variables, and user-secrets).
        var current = builder.Build();

        var overrides = new Dictionary<string, string?>();
        foreach (var (key, environmentVariable) in KeyToEnvironmentVariable)
        {
            if (!string.IsNullOrWhiteSpace(current[key]))
                continue;

            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
                overrides[key] = value;
        }

        if (overrides.Count > 0)
            builder.AddInMemoryCollection(overrides);

        return builder;
    }
}
