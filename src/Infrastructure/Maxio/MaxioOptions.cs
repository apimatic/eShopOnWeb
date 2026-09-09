using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Values are bound from the
/// "Maxio" configuration section (user-secrets / environment variables); no value
/// is hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The site API key, used as the Basic-auth username (password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Advanced Billing site subdomain (e.g. "acme" for https://acme.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The hosting environment: "US" (default) or "EU".
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from Subdomain + Environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. Verified against the official Maxio
    /// Advanced Billing SDKs: US sites are served at https://{site}.chargify.com
    /// and EU sites at https://{site}.ebilling.maxio.com.
    /// </summary>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/'));
        }

        return Environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
            ? new Uri($"https://{Subdomain}.ebilling.maxio.com")
            : new Uri($"https://{Subdomain}.chargify.com");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain (or Maxio:BaseUrl) is not configured. Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }
}
