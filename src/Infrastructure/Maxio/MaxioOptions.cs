using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Secret values (e.g. the API key) are
/// supplied via user-secrets or environment variables, never the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string ApiKeyEnvVar = "MAXIO_API_KEY";
    public const string SubdomainEnvVar = "MAXIO_SITE_SUBDOMAIN";
    public const string EnvironmentEnvVar = "MAXIO_ENVIRONMENT";
    public const string ProductFamilyEnvVar = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    /// <summary>
    /// Maxio API key, used as the Basic-auth username (password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain of the Advanced Billing site, e.g. "acme".
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Hosted environment: "US" (default) or "EU". Only used when
    /// <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address
    /// instead of deriving one from the subdomain/environment.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the API base URL per the OpenAPI spec's server configuration:
    /// BaseUrl override wins verbatim; otherwise derive from environment
    /// and subdomain (US: https://{site}.chargify.com, EU: https://{site}.ebilling.maxio.com).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var subdomain = GuardedSubdomain();
        return string.Equals(Environment, "EU", System.StringComparison.OrdinalIgnoreCase)
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";
    }

    private string GuardedSubdomain()
    {
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio:Subdomain is required (set it via the {SubdomainEnvVar} environment variable or user-secrets).");
        }
        return Subdomain.Trim();
    }
}