using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section; values are supplied via
/// environment variables / user-secrets and must never be committed to the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (config key: Maxio:ApiKey, env: MAXIO_API_KEY).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (config key: Maxio:Subdomain, env: MAXIO_SITE_SUBDOMAIN).
    /// Used to derive the default base URL (https://{subdomain}.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans
    /// (config key: Maxio:ProductFamilyHandle, env: MAXIO_DEFAULT_PRODUCT_FAMILY).
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base URL override (config key: Maxio:BaseUrl).
    /// When set, it is used instead of the URL derived from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address: BaseUrl verbatim when provided, otherwise derived from the subdomain.
    /// </summary>
    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration error: either Maxio:BaseUrl or Maxio:Subdomain must be provided.");
        }

        return $"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com";
    }

    /// <summary>
    /// Returns the list of missing required configuration keys (empty when valid).
    /// BaseUrl overrides the Subdomain requirement.
    /// </summary>
    public IReadOnlyList<string> GetMissingRequiredKeys()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add("Maxio:ApiKey");
        }
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add("Maxio:ProductFamilyHandle");
        }
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            missing.Add("Maxio:Subdomain (or Maxio:BaseUrl)");
        }
        return missing;
    }
}
