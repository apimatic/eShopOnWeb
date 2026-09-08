using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maps the process-level Maxio environment variables onto the <c>Maxio</c>
/// configuration section so that settings can be bound with the exact
/// <see cref="MaxioOptions"/> keys (Maxio:ApiKey, Maxio:Subdomain,
/// Maxio:Environment, Maxio:ProductFamilyHandle and the optional Maxio:BaseUrl).
/// </summary>
/// <remarks>
/// Values are never written into the repository. Environment variables simply take
/// precedence over any other configuration source because this source is added last.
/// </remarks>
public static class MaxioConfigurationExtensions
{
    public const string ApiKeyEnvironmentVariable = "MAXIO_API_KEY";
    public const string SubdomainEnvironmentVariable = "MAXIO_SITE_SUBDOMAIN";
    public const string EnvironmentEnvironmentVariable = "MAXIO_ENVIRONMENT";
    public const string ProductFamilyEnvironmentVariable = "MAXIO_DEFAULT_PRODUCT_FAMILY";
    public const string BaseUrlEnvironmentVariable = "MAXIO_BASE_URL";

    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}"] =
                Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
            [$"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}"] =
                Environment.GetEnvironmentVariable(SubdomainEnvironmentVariable),
            [$"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}"] =
                Environment.GetEnvironmentVariable(ProductFamilyEnvironmentVariable)
        };

        // Optional keys are only added when actually present so they never shadow
        // values supplied through user-secrets or appsettings.
        AddIfPresent(values, EnvironmentEnvironmentVariable, $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Environment)}");
        AddIfPresent(values, BaseUrlEnvironmentVariable, $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)}");

        builder.AddInMemoryCollection(values);

        return builder;
    }

    private static void AddIfPresent(IDictionary<string, string?> values, string environmentVariable, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configKey] = value;
        }
    }
}
