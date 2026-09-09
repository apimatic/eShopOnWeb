namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section (user-secrets / environment variables);
/// no value is ever hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string ApiKeyEnvVar = "MAXIO_API_KEY";
    public const string SubdomainEnvVar = "MAXIO_SITE_SUBDOMAIN";
    public const string EnvironmentEnvVar = "MAXIO_ENVIRONMENT";
    public const string ProductFamilyEnvVar = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    /// <summary>Maxio API key (sent as the Basic-auth username).</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The Maxio site subdomain.</summary>
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>Maxio hosting environment: US (default) or EU, per the spec's server configuration.</summary>
    public string Environment { get; init; } = "US";

    /// <summary>The product family (by stable handle) whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>
    /// Optional verbatim override of the API base address; when set it is used
    /// as-is instead of deriving the URL from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Resolves the API base address. Spec server templating:
    /// US → https://{site}.chargify.com, EU → https://{site}.ebilling.maxio.com.
    /// <see cref="BaseUrl"/> wins when set.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var host = string.Equals(Environment, "EU", System.StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return $"https://{Subdomain}.{host}";
    }
}
