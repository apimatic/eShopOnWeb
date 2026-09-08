using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string ApiKeyEnvironmentVariable = "MAXIO_API_KEY";
    public const string SubdomainEnvironmentVariable = "MAXIO_SITE_SUBDOMAIN";
    public const string ProductFamilyHandleEnvironmentVariable = "MAXIO_DEFAULT_PRODUCT_FAMILY";
    public const string BaseUrlEnvironmentVariable = "MAXIO_BASE_URL";
    public const string EnvironmentEnvironmentVariable = "MAXIO_ENVIRONMENT";

    /// <summary>
    /// Maps the canonical MAXIO_* environment variables into the Maxio configuration
    /// section. Explicitly configured values (for example from .NET user secrets or
    /// appsettings) always win; environment variables act as the fallback source so a
    /// fresh checkout runs with nothing more than the sandbox credentials exported.
    /// </summary>
    public static ConfigurationManager AddMaxioEnvironmentFallback(this ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        AddEnvironmentOverride(overrides, "Maxio:ApiKey", configuration["Maxio:ApiKey"], ApiKeyEnvironmentVariable);
        AddEnvironmentOverride(overrides, "Maxio:Subdomain", configuration["Maxio:Subdomain"], SubdomainEnvironmentVariable);
        AddEnvironmentOverride(overrides, "Maxio:ProductFamilyHandle", configuration["Maxio:ProductFamilyHandle"], ProductFamilyHandleEnvironmentVariable);
        AddEnvironmentOverride(overrides, "Maxio:BaseUrl", configuration["Maxio:BaseUrl"], BaseUrlEnvironmentVariable);

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }

        return configuration;
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioBillingOptions>(configuration.GetSection(MaxioBillingOptions.CONFIG_SECTION_NAME));

        services.PostConfigure<MaxioBillingOptions>(options =>
        {
            options.Environment = Environment.GetEnvironmentVariable(EnvironmentEnvironmentVariable) ?? options.Environment ?? "US";
        });

        services.AddHttpClient<IMaxioBillingService, MaxioBillingService>();

        return services;
    }

    private static void AddEnvironmentOverride(Dictionary<string, string?> overrides, string key, string? configuredValue, string environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return;
        }

        string? environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            overrides[key] = environmentValue;
        }
    }
}
