namespace Microsoft.eShopWeb.Infrastructure.Maxio;

using System;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio"
/// configuration section. Values are supplied via user-secrets / environment
/// variables and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string ApiKeyEnvVar = "MAXIO_API_KEY";
    public const string SubdomainEnvVar = "MAXIO_SITE_SUBDOMAIN";
    public const string EnvironmentEnvVar = "MAXIO_ENVIRONMENT";
    public const string ProductFamilyHandleEnvVar = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    /// <summary>
    /// Maxio Advanced Billing API key (used as the Basic-auth username).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (e.g. "acme" for https://acme.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Maxio environment (e.g. "US" or "EU"); only used to derive the base URL.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override. When set it is used as-is
    /// instead of deriving the base URL from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address: the BaseUrl override when set, otherwise derived
    /// from the environment and subdomain.
    /// </summary>
    public string EffectiveBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl.TrimEnd('/') + "/";
            }

            var host = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
                ? $"{Subdomain}.ebilling.maxio.com"
                : $"{Subdomain}.chargify.com";
            return $"https://{host}/";
        }
    }
}
