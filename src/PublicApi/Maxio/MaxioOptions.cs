using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
///
/// Bound from the "Maxio" configuration section. Values are provided through
/// environment variables (see <see cref="ApplyFromEnvironment"/>) or .NET
/// user-secrets; no secret value is ever stored in the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Basic-auth API key (username) for the Maxio site. From MAXIO_API_KEY.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain (e.g. "cp-exp-6"). From MAXIO_SITE_SUBDOMAIN.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the subscription plans. From MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim API base address override. When set it is used as-is
    /// instead of deriving a base address from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment used to pick the server template ("US" or "EU"). From MAXIO_ENVIRONMENT.
    /// </summary>
    public string? Environment { get; set; } = "US";

    public string BuildApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                $"Maxio is not configured: '{SectionName}:Subdomain' (env MAXIO_SITE_SUBDOMAIN) or '{SectionName}:BaseUrl' is required.");
        }

        var environment = string.IsNullOrWhiteSpace(Environment)
            ? "US"
            : Environment.Trim();
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain.Trim()}.ebilling.maxio.com"
            : $"{Subdomain.Trim()}.chargify.com";
        return $"https://{host}";
    }

    /// <summary>
    /// Pushes the MAXIO_* environment variables into the "Maxio" configuration
    /// section using the exact binding keys required by the integration.
    /// Existing values (e.g. from user-secrets or appsettings) are preserved
    /// when the corresponding environment variable is absent.
    /// </summary>
    public static void ApplyFromEnvironment(IConfigurationBuilder configuration)
    {
        var mappings = new (string Env, string Key)[]
        {
            ("MAXIO_API_KEY", "ApiKey"),
            ("MAXIO_SITE_SUBDOMAIN", "Subdomain"),
            ("MAXIO_ENVIRONMENT", "Environment"),
            ("MAXIO_DEFAULT_PRODUCT_FAMILY", "ProductFamilyHandle"),
            ("MAXIO_BASE_URL", "BaseUrl"),
        };

        var values = new Dictionary<string, string?>();
        foreach (var (env, key) in mappings)
        {
            var value = System.Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[$"{SectionName}:{key}"] = value;
            }
        }

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }
    }
}
